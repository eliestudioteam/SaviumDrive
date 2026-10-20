// Program.cs
// Punto de entrada propio (en vez del que genera XAML) para que solo haya una Savium Drive abierta:
// si ya hay una, esta le pasa su activación (abrir desde el navegador con saviumdrive://, otro doble clic
// en el .exe...) y se cierra. La que ya estaba abierta trae su ventana al frente.
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SaviumDrive
{
    public static class Program
    {
        private const string ClaveInstancia = "SaviumDrive-Principal";

        /// <summary>Argumento de ReiniciarAplicacion: la instancia anterior aún se está cerrando.</summary>
        public const string ArgumentoReinicio = "--reinicio";

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(uint dwProcessId);

        [DllImport("ole32.dll")]
        private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, uint nHandles, IntPtr[] pHandles, out uint dwIndex);

        [STAThread]
        private static int Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            if (PasarASiYaHayOtra(args)) return 0;

            Application.Start(p =>
            {
                var contexto = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(contexto);
                _ = new App();
            });
            return 0;
        }

        /// <summary>True si ya había una Savium Drive abierta y se le pasó la activación.</summary>
        private static bool PasarASiYaHayOtra(string[] args)
        {
            try
            {
                var activacion = AppInstance.GetCurrent().GetActivatedEventArgs();
                var principal = AppInstance.FindOrRegisterForKey(ClaveInstancia);

                // Tras reiniciar (cambio de idioma) la instancia anterior tarda un momento en cerrarse
                if (!principal.IsCurrent && args.Contains(ArgumentoReinicio))
                {
                    for (int i = 0; i < 50 && !principal.IsCurrent; i++)
                    {
                        Thread.Sleep(100);
                        principal = AppInstance.FindOrRegisterForKey(ClaveInstancia);
                    }
                }

                if (principal.IsCurrent)
                {
                    principal.Activated += App.AlActivarDesdeOtraInstancia;
                    return false;
                }

                // Esta instancia la abrió el usuario (o el navegador): puede ceder el primer plano a la principal
                AllowSetForegroundWindow(principal.ProcessId);

                // La redirección no debe bloquear el hilo STA sin atender mensajes COM (lo indica Windows App SDK)
                using var hecho = new ManualResetEvent(false);
                Task.Run(() =>
                {
                    try { principal.RedirectActivationToAsync(activacion).AsTask().Wait(); }
                    catch { }
                    finally { hecho.Set(); }
                });
                CoWaitForMultipleObjects(0, 10000, 1, new[] { hecho.SafeWaitHandle.DangerousGetHandle() }, out _);
                return true;
            }
            catch
            {
                // Si algo falla, abrir normalmente antes que no abrir
                return false;
            }
        }
    }
}
