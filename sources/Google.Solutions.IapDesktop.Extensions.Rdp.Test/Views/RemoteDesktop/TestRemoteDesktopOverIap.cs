﻿//
// Copyright 2020 Google LLC
//
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
//

using Google.Solutions.Common.Test.Integration;
using Google.Solutions.IapDesktop.Application.ObjectModel;
using Google.Solutions.IapDesktop.Application.Services.Adapters;
using Google.Solutions.IapDesktop.Application.Services.Integration;
using Google.Solutions.IapDesktop.Application.Util;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Google.Apis.Auth.OAuth2;
using Google.Solutions.Common.Locator;
using Google.Solutions.IapDesktop.Extensions.Rdp.Views.RemoteDesktop;
using Google.Solutions.IapDesktop.Application.Test.Views;
using Google.Solutions.IapDesktop.Extensions.Rdp.Services.Connection;
using System;

namespace Google.Solutions.IapDesktop.Extensions.Rdp.Test.Views.RemoteDesktop
{
    [TestFixture]
    [Category("IntegrationTest")]
    [Category("IAP")]
    public class TestRemoteDesktopOverIap : WindowTestFixtureBase
    {
        [Test]
        public async Task WhenCredentialsInvalid_ThenErrorIsShownAndWindowIsClosed(
            [WindowsInstance] ResourceTask<InstanceLocator> testInstance,
            [Credential(Role = PredefinedRole.IapTunnelUser)] ResourceTask<ICredential> credential)
        {
            var locator = await testInstance;

            using (var tunnel = RdpTunnel.Create(
                locator,
                await credential))
            {
                var settings = VmInstanceConnectionSettings.CreateNew(
                    locator.ProjectId, 
                    locator.Name);
                settings.Username.StringValue = "wrong";
                settings.Password.Value = SecureStringExtensions.FromClearText("wrong");
                settings.AuthenticationLevel.EnumValue = RdpAuthenticationLevel.NoServerAuthentication;
                settings.UserAuthenticationBehavior.EnumValue = RdpUserAuthenticationBehavior.AbortOnFailure;
                settings.DesktopSize.EnumValue = RdpDesktopSize.ClientSize;

                var rdpService = new RemoteDesktopConnectionBroker(this.serviceProvider);
                var session = rdpService.Connect(
                    locator,
                    "localhost",
                    (ushort)tunnel.LocalPort,
                    settings);

                AwaitEvent<ConnectionFailedEvent>();
                Assert.IsNotNull(this.ExceptionShown);
                Assert.IsInstanceOf(typeof(RdpDisconnectedException), this.ExceptionShown);
                Assert.AreEqual(2055, ((RdpDisconnectedException)this.ExceptionShown).DisconnectReason);
            }
        }

        //
        // There's no reliable way to dismiss the warning/error, so these tests seem 
        // challenging to implement.
        //
        //[Test]
        //public void WhenAttemptServerAuthentication_ThenWarningIsShown()
        //{
        //}

        //[Test]
        //public void WhenRequireServerAuthentication_ThenConnectionFails(
        //    [WindowsInstance] ResourceTask<InstanceLocator> testInstance)
        //{
        //}

        [Test]
        public async Task WhenCredentialsValid_ThenConnectingSucceeds(
            [Values(RdpConnectionBarState.AutoHide, RdpConnectionBarState.Off, RdpConnectionBarState.Pinned)]
            RdpConnectionBarState connectionBarState,

            [Values(RdpDesktopSize.ClientSize, RdpDesktopSize.ScreenSize)]
            RdpDesktopSize desktopSize,

            [Values(RdpAudioMode.DoNotPlay, RdpAudioMode.PlayLocally, RdpAudioMode.PlayOnServer)]
            RdpAudioMode audioMode,

            [Values(RdpRedirectClipboard.Disabled, RdpRedirectClipboard.Enabled)]
            RdpRedirectClipboard redirectClipboard,

            // Use a slightly larger machine type as all this RDP'ing consumes a fair
            // amount of memory.
            [WindowsInstance(MachineType = "n1-standard-2")] ResourceTask<InstanceLocator> testInstance,
            [Credential(Role = PredefinedRole.IapTunnelUser)] ResourceTask<ICredential> credential)
        {
            var locator = await testInstance;

            using (var tunnel = RdpTunnel.Create(
                locator,
                await credential))
            using (var gceAdapter = new ComputeEngineAdapter(this.serviceProvider.GetService<IAuthorizationAdapter>()))
            {
                var credentials = await gceAdapter.ResetWindowsUserAsync(
                    locator,
                    CreateRandomUsername(),
                    TimeSpan.FromSeconds(60),
                    CancellationToken.None);

                var settings = VmInstanceConnectionSettings.CreateNew(
                    locator.ProjectId,
                    locator.Name);
                settings.Username.StringValue = credentials.UserName;
                settings.Password.Value = credentials.SecurePassword;
                settings.ConnectionBar.EnumValue = connectionBarState;
                settings.DesktopSize.EnumValue = desktopSize;
                settings.AudioMode.EnumValue = audioMode;
                settings.RedirectClipboard.EnumValue = redirectClipboard;
                settings.AuthenticationLevel.EnumValue = RdpAuthenticationLevel.NoServerAuthentication;
                settings.BitmapPersistence.EnumValue = RdpBitmapPersistence.Disabled;

                var rdpService = new RemoteDesktopConnectionBroker(this.serviceProvider);
                var session = rdpService.Connect(
                    locator,
                    "localhost",
                    (ushort)tunnel.LocalPort,
                    settings);

                AwaitEvent<ConnectionSuceededEvent>();
                Assert.IsNull(this.ExceptionShown);


                ConnectionClosedEvent expectedEvent = null;

                this.serviceProvider.GetService<IEventService>()
                    .BindHandler<ConnectionClosedEvent>(e =>
                    {
                        expectedEvent = e;
                    });
                session.Close();

                Assert.IsNotNull(expectedEvent);
            }
        }

        [Test, Ignore("Unreliable in CI")]
        public async Task WhenSigningOutPerSendKeys_ThenWindowIsClosed(
            [WindowsInstance(ImageFamily = WindowsInstanceAttribute.WindowsServer2019)]
            ResourceTask<InstanceLocator> testInstance,
            [Credential(Role = PredefinedRole.IapTunnelUser)] ResourceTask<ICredential> credential)
        {
            var locator = await testInstance;

            using (var tunnel = RdpTunnel.Create(
                locator,
                await credential))
            using (var gceAdapter = new ComputeEngineAdapter(this.serviceProvider.GetService<IAuthorizationAdapter>()))
            {
                var credentials = await gceAdapter.ResetWindowsUserAsync(
                       locator,
                       CreateRandomUsername(),
                       TimeSpan.FromSeconds(60),
                       CancellationToken.None);

                var settings = VmInstanceConnectionSettings.CreateNew(
                    locator.ProjectId,
                    locator.Name);
                settings.Username.StringValue = credentials.UserName;
                settings.Password.Value = credentials.SecurePassword;
                settings.AuthenticationLevel.EnumValue = RdpAuthenticationLevel.NoServerAuthentication;
                settings.BitmapPersistence.EnumValue = RdpBitmapPersistence.Disabled;
                settings.DesktopSize.EnumValue = RdpDesktopSize.ClientSize;

                var rdpService = new RemoteDesktopConnectionBroker(this.serviceProvider);
                var session = (RemoteDesktopPane)rdpService.Connect(
                    locator,
                    "localhost",
                    (ushort)tunnel.LocalPort,
                    settings);

                AwaitEvent<ConnectionSuceededEvent>();

                Thread.Sleep(5000);
                session.ShowSecurityScreen();
                Thread.Sleep(1000);
                session.SendKeys(Keys.Menu, Keys.S); // Sign out.

                AwaitEvent<ConnectionClosedEvent>();
                Assert.IsNull(this.ExceptionShown);
            }
        }
    }
}
