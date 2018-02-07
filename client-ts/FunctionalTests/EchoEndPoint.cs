// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;

namespace FunctionalTests
{
    public class EchoEndPoint : EndPoint
    {
        public async override Task OnConnectedAsync(ConnectionContext connection)
        {
            var result = await connection.Transport.Input.ReadAsync();

            try
            {
                foreach (var segment in result.Buffer)
                {
                    await connection.Transport.Output.WriteAsync(segment);
                }
            }
            finally
            {
                connection.Transport.Input.AdvanceTo(result.Buffer.End);
            }
        }
    }
}
