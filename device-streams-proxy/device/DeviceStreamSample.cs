﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Azure.Devices.Samples.Common;
using System;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Devices.Client.Samples
{
    public class DeviceStreamSample
    {
        private DeviceClient _deviceClient;
        private String _host;
        private int _port;

        public DeviceStreamSample(DeviceClient deviceClient, String host, int port)
        {
            _deviceClient = deviceClient;
            _host = host;
            _port = port;
        }

        private static async Task HandleIncomingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (remoteStream.State == WebSocketState.Open)
            {
                var receiveResult = await remoteStream.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (receiveResult.Count > 0)
                {
                    Console.WriteLine($"incoming:{receiveResult.Count}byte");
                }

                await localStream.WriteAsync(buffer, 0, receiveResult.Count).ConfigureAwait(false);
            }
        }

        private static async Task HandleOutgoingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (localStream.CanRead)
            {
                int receiveCount = await localStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

                if (receiveCount > 0)
                {
                    Console.WriteLine($"outgoing:{receiveCount}byte");
                }

                await remoteStream.SendAsync(new ArraySegment<byte>(buffer, 0, receiveCount), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task RunSampleAsync()
        {
            await RunSampleAsync(true).ConfigureAwait(false);
        }

        public async Task RunSampleAsync(bool acceptDeviceStreamingRequest)
        {
            using (var cancellationTokenSource = new CancellationTokenSource(new TimeSpan(0, 5, 0)))
            {
                while (true)
                {
                    DeviceStreamRequest streamRequest = await _deviceClient.WaitForDeviceStreamRequestAsync(cancellationTokenSource.Token).ConfigureAwait(false);

                    if (streamRequest != null)
                    {
                        if (acceptDeviceStreamingRequest)
                        {
                            Handle(cancellationTokenSource, streamRequest);
                        }
                        else
                        {
                            await _deviceClient.RejectDeviceStreamRequestAsync(streamRequest, cancellationTokenSource.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        private async void Handle(CancellationTokenSource cancellationTokenSource, DeviceStreamRequest streamRequest)
        {
            try
            {
                await _deviceClient.AcceptDeviceStreamRequestAsync(streamRequest, cancellationTokenSource.Token).ConfigureAwait(false);

                using (ClientWebSocket webSocket = await DeviceStreamingCommon.GetStreamingClientAsync(streamRequest.Url, streamRequest.AuthorizationToken, cancellationTokenSource.Token).ConfigureAwait(false))
                {
                    using (TcpClient tcpClient = new TcpClient())
                    {
                        await tcpClient.ConnectAsync(_host, _port).ConfigureAwait(false);

                        using (NetworkStream localStream = tcpClient.GetStream())
                        {
                            Console.WriteLine("Starting streaming");

                            await Task.WhenAny(
                                HandleIncomingDataAsync(localStream, webSocket, cancellationTokenSource.Token),
                                HandleOutgoingDataAsync(localStream, webSocket, cancellationTokenSource.Token)).ConfigureAwait(false);

                            localStream.Close();
                        }
                    }

                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, String.Empty, cancellationTokenSource.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Got an exception: {0}", ex);
            }
        }
    }
}
