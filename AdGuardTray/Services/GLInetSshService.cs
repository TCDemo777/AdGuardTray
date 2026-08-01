using System;
using System.Threading.Tasks;
using Renci.SshNet;

namespace AdGuardTray.Services
{
    public class GLInetSshService
    {
        private readonly string _ip;
        private readonly string _username;
        private readonly string _password;


        public GLInetSshService(
            string ip,
            string username,
            string password)
        {
            _ip = ip;
            _username = username;
            _password = password;
        }





        public Task<string> RunCommandAsync(
            string command)
        {
            return Task.Run(() =>
            {
                try
                {
                    using var ssh =
                        new SshClient(
                            _ip,
                            _username,
                            _password);


                    // Prevent long hangs
                    ssh.ConnectionInfo.Timeout =
                        TimeSpan.FromSeconds(5);



                    ssh.Connect();



                    if (!ssh.IsConnected)
                    {
                        return
                            "SSH connection failed.";
                    }



                    var result =
                        ssh.RunCommand(command);



                    ssh.Disconnect();



                    return
                        result.Result +
                        Environment.NewLine +
                        result.Error;
                }


                catch (Renci.SshNet.Common.SshAuthenticationException)
                {
                    return
                        "SSH_AUTH_FAILED";
                }


                catch (Renci.SshNet.Common.SshConnectionException)
                {
                    return
                        "SSH_CONNECTION_FAILED";
                }


                catch (System.Net.Sockets.SocketException)
                {
                    return
                        "SSH_NETWORK_FAILED";
                }


                catch (Exception ex)
                {
                    return
                        "SSH_ERROR: " +
                        ex.Message;
                }
            });
        }
    }
}