using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace SaviumDrive.Services
{
    public class NetworkService
    {
        public event EventHandler<bool> ConnectivityChanged;

        private bool _lastStatus = true;
        private System.Threading.Timer _checkTimer;

        public NetworkService()
        {
            // Iniciar monitoreo cada 5 segundos
            _checkTimer = new System.Threading.Timer(async _ => await CheckAndNotify(), null, 0, 5000);
        }

        public async Task<bool> IsConnectedAsync()
        {
            try
            {
                // Verificar si hay conexión de red disponible
                if (!NetworkInterface.GetIsNetworkAvailable())
                    return false;

                // Intentar ping a servidores confiables
                using (var ping = new Ping())
                {
                    try
                    {
                        // Intentar Google DNS
                        var reply = await ping.SendPingAsync("8.8.8.8", 3000);
                        if (reply.Status == IPStatus.Success)
                            return true;

                        // Si falla, intentar Cloudflare DNS
                        reply = await ping.SendPingAsync("1.1.1.1", 3000);
                        return reply.Status == IPStatus.Success;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task CheckAndNotify()
        {
            bool currentStatus = await IsConnectedAsync();

            if (currentStatus != _lastStatus)
            {
                _lastStatus = currentStatus;
                ConnectivityChanged?.Invoke(this, currentStatus);
            }
        }

        public void Dispose()
        {
            _checkTimer?.Dispose();
        }
    }
}