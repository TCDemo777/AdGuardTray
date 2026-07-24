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
                using var ssh =
                    new SshClient(
                        _ip,
                        _username,
                        _password);


                ssh.Connect();


                var result =
                    ssh.RunCommand(command);


                ssh.Disconnect();


                return
                    result.Result +
                    Environment.NewLine +
                    result.Error;
            });
        }
    }
}