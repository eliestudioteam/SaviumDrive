using Microsoft.Windows.ApplicationModel.Resources;
using System;

namespace SaviumDrive.Helpers
{
    /// <summary>
    /// Textos traducidos de la app (Strings/es-ES y Strings/en-US, archivos .resw).
    /// El idioma lo elige App al arrancar según la configuración.
    /// </summary>
    public static class ResourceHelper
    {
        private static readonly ResourceManager _resourceManager = new ResourceManager();
        private static readonly ResourceMap _mainResourceMap = _resourceManager.MainResourceMap;

        /// <summary>
        /// Texto con la clave indicada. Las claves de propiedades XAML se escriben con barra
        /// (por ejemplo "ChangelogTitle/Text" para "ChangelogTitle.Text"). Si la clave no existe devuelve
        /// "[clave]", para que el texto que falta se vea en pantalla.
        /// </summary>
        public static string GetString(string key)
        {
            try
            {
                // Los .resw de la app quedan bajo "Resources/"; se prueba también en la raíz del mapa
                var candidate = _mainResourceMap.TryGetValue($"Resources/{key}");
                if (candidate == null)
                {
                    candidate = _mainResourceMap.TryGetValue(key);
                }

                if (candidate != null)
                {
                    return candidate.ValueAsString.Replace("\\n", "\n");
                }
            }
            catch
            {
                // Un texto que no se encuentra nunca debe cerrar la app
            }

            return $"[{key}]";
        }

        /// <summary>Texto con formato: sustituye {0}, {1}... por los argumentos.</summary>
        public static string GetString(string key, params object[] args)
        {
            string template = GetString(key);
            if (string.IsNullOrEmpty(template) || template.StartsWith("[")) return template;

            try
            {
                return string.Format(template, args);
            }
            catch
            {
                return template;
            }
        }
    }
}