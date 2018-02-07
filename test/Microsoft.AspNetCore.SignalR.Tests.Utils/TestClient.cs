// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class TestClient : IDisposable
    {
        private static int _id;
        private readonly HubProtocolReaderWriter _protocolReaderWriter;
        private readonly IInvocationBinder _invocationBinder;
        private CancellationTokenSource _cts;

        public DefaultConnectionContext Connection { get; }
        public IDuplexPipe Application { get; }
        public Task Connected => ((TaskCompletionSource<bool>)Connection.Metadata["ConnectedTask"]).Task;

        public TestClient(bool synchronousCallbacks = false, IHubProtocol protocol = null, IInvocationBinder invocationBinder = null, bool addClaimId = false)
        {
            var options = new PipeOptions(readerScheduler: synchronousCallbacks ? PipeScheduler.Inline : null);
            var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, options);
            Connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pair.Transport, pair.Application);
            Application = Connection.Transport;

            var claimValue = Interlocked.Increment(ref _id).ToString();
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, claimValue) };
            if (addClaimId)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, claimValue));
            }

            Connection.User = new ClaimsPrincipal(new ClaimsIdentity(claims));
            Connection.Metadata["ConnectedTask"] = new TaskCompletionSource<bool>();

            protocol = protocol ?? new JsonHubProtocol();
            _protocolReaderWriter = new HubProtocolReaderWriter(protocol, new PassThroughEncoder());
            _invocationBinder = invocationBinder ?? new DefaultInvocationBinder();

            _cts = new CancellationTokenSource();

            using (var memoryStream = new MemoryStream())
            {
                NegotiationProtocol.WriteMessage(new NegotiationMessage(protocol.Name), memoryStream);
                Connection.Application.Output.Write(memoryStream.ToArray());
                Connection.Application.Output.Commit();
            }
        }

        public async Task<IList<HubMessage>> StreamAsync(string methodName, params object[] args)
        {
            var invocationId = await SendStreamInvocationAsync(methodName, args);

            var messages = new List<HubMessage>();
            while (true)
            {
                var message = await ReadAsync();

                if (message == null)
                {
                    throw new InvalidOperationException("Connection aborted!");
                }

                if (message is HubInvocationMessage hubInvocationMessage && !string.Equals(hubInvocationMessage.InvocationId, invocationId))
                {
                    throw new NotSupportedException("TestClient does not support multiple outgoing invocations!");
                }

                switch (message)
                {
                    case StreamItemMessage _:
                        messages.Add(message);
                        break;
                    case CompletionMessage _:
                        messages.Add(message);
                        return messages;
                    default:
                        throw new NotSupportedException("TestClient does not support receiving invocations!");
                }
            }
        }

        public async Task<CompletionMessage> InvokeAsync(string methodName, params object[] args)
        {
            var invocationId = await SendInvocationAsync(methodName, nonBlocking: false, args: args);

            while (true)
            {
                var message = await ReadAsync();

                if (message == null)
                {
                    throw new InvalidOperationException("Connection aborted!");
                }

                if (message is HubInvocationMessage hubInvocationMessage && !string.Equals(hubInvocationMessage.InvocationId, invocationId))
                {
                    throw new NotSupportedException("TestClient does not support multiple outgoing invocations!");
                }

                switch (message)
                {
                    case StreamItemMessage result:
                        throw new NotSupportedException("Use 'StreamAsync' to call a streaming method");
                    case CompletionMessage completion:
                        return completion;
                    case PingMessage _:
                        // Pings are ignored
                        break;
                    default:
                        throw new NotSupportedException("TestClient does not support receiving invocations!");
                }
            }
        }

        public Task<string> SendInvocationAsync(string methodName, params object[] args)
        {
            return SendInvocationAsync(methodName, nonBlocking: false, args: args);
        }

        public Task<string> SendInvocationAsync(string methodName, bool nonBlocking, params object[] args)
        {
            var invocationId = nonBlocking ? null : GetInvocationId();
            return SendHubMessageAsync(new InvocationMessage(invocationId, methodName,
                argumentBindingException: null, arguments: args));
        }

        public Task<string> SendStreamInvocationAsync(string methodName, params object[] args)
        {
            var invocationId = GetInvocationId();
            return SendHubMessageAsync(new StreamInvocationMessage(invocationId, methodName,
                argumentBindingException: null, arguments: args));
        }

        public async Task<string> SendHubMessageAsync(HubMessage message)
        {
            var payload = _protocolReaderWriter.WriteMessage(message);
            await Application.Output.WriteAsync(payload);
            return message is HubInvocationMessage hubMessage ? hubMessage.InvocationId : null;
        }

        public async Task<HubMessage> ReadAsync()
        {
            while (true)
            {
                var message = TryRead();

                if (message == null)
                {
                    var result = await Connection.Transport.Input.ReadAsync();
                    var buffer = result.Buffer;

                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            continue;
                        }

                        if (result.IsCompleted)
                        {
                            return null;
                        }
                    }
                    finally
                    {
                        Connection.Transport.Input.AdvanceTo(buffer.Start);
                    }
                }
                else
                {
                    return message;
                }
            }
        }

        public HubMessage TryRead()
        {
            if (Application.Input.TryRead(out var result) &&
                _protocolReaderWriter.ReadMessages(result.Buffer, _invocationBinder, out var messages, out var consumed, out var examined))
            {
                Application.Input.AdvanceTo(consumed, examined);
                return messages[0];
            }
            return null;
        }

        public void Dispose()
        {
            _cts.Cancel();


            Connection.Application.Input.Complete();
            Connection.Application.Output.Complete();

            Connection.Transport.Input.Complete();
            Connection.Transport.Output.Complete();
        }

        private static string GetInvocationId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private class DefaultInvocationBinder : IInvocationBinder
        {
            public Type[] GetParameterTypes(string methodName)
            {
                // TODO: Possibly support actual client methods
                return new[] { typeof(object) };
            }

            public Type GetReturnType(string invocationId)
            {
                return typeof(object);
            }
        }
    }
}
