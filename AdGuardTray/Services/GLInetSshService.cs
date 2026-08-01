using System;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace AdGuardTray.Services
{
    public sealed class GLInetSshService : IDisposable
    {
        private readonly string _ip;
        private readonly string _username;
        private readonly string _password;
        private readonly SemaphoreSlim _commandGate = new(1, 1);
        private SshClient? _client;
        private bool _disposed;

        public GLInetSshService(
            string ip,
            string username,
            string password)
        {
            _ip = ip;
            _username = username;
            _password = password;
        }

        public Task<string> RunCommandAsync(string command)
        {
            return RunCommandAsync(command, CancellationToken.None);
        }

        public async Task<string> RunCommandAsync(
            string command,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(command);

            await _commandGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                return await Task.Run(
                        () => ExecuteCommand(command),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _commandGate.Release();
            }
        }

        private string ExecuteCommand(string command)
        {
            try
            {
                EnsureConnected();

                using SshCommand result =
                    _client!.CreateCommand(command);

                result.CommandTimeout =
                    TimeSpan.FromSeconds(20);

                string output = result.Execute();

                return output +
                    Environment.NewLine +
                    result.Error;
            }
            catch (SshAuthenticationException)
            {
                ResetClient();
                return "SSH_AUTH_FAILED";
            }
            catch (SshConnectionException)
            {
                ResetClient();
                return "SSH_CONNECTION_FAILED";
            }
            catch (System.Net.Sockets.SocketException)
            {
                ResetClient();
                return "SSH_NETWORK_FAILED";
            }
            catch (Exception ex)
            {
                ResetClient();
                return "SSH_ERROR: " + ex.Message;
            }
        }

        private void EnsureConnected()
        {
            if (_client is { IsConnected: true })
            {
                return;
            }

            ResetClient();

            _client = new SshClient(
                _ip,
                _username,
                _password)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

            _client.ConnectionInfo.Timeout =
                TimeSpan.FromSeconds(5);

            _client.Connect();

            if (!_client.IsConnected)
            {
                throw new SshConnectionException(
                    "SSH connection failed.");
            }
        }

        private void ResetClient()
        {
            if (_client is null)
            {
                return;
            }

            try
            {
                if (_client.IsConnected)
                {
                    _client.Disconnect();
                }
            }
            catch
            {
                // The connection is already unusable. Disposal below is enough.
            }

            _client.Dispose();
            _client = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _commandGate.Wait();
            try
            {
                ResetClient();
            }
            finally
            {
                _commandGate.Release();
                _commandGate.Dispose();
            }
        }
    }
}
