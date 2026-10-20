using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace SaviumDrive.Services
{
    /// <summary>
    /// Comprueba cada 5 segundos si hay conexión a Internet (ping a 8.8.8.8 o 1.1.1.1) y avisa con
    /// <see cref="ConnectivityChanged"/> cuando cambia. Lo usa el panel de inicio para mostrar el estado.
    /// </summary>
    public class NetworkService
    {
        public event EventHandler<bool> ConnectivityChanged;

        private bool _lastStatus = true;
        private System.Threading.Timer _checkTimer;

        public NetworkService()
        {
            _checkTimer = new System.Threading.Timer(async _ => await CheckAndNotify(), null, 0, 5000);
        }

        public async Task<bool> IsConnectedAsync()
        {
            try
            {
                // Sin ninguna red no hace falta hacer ping
                if (!NetworkInterface.GetIsNetworkAvailable())
                    return false;

                // Ping a los DNS públicos de Google y, si falla, de Cloudflare
                using (var ping = new Ping())
                {
                    try
                    {
                        var reply = await ping.SendPingAsync("8.8.8.8", 3000);
                        if (reply.Status == IPStatus.Success)
                            return true;

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