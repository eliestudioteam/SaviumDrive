using Microsoft.Windows.ApplicationModel.Resources;
using System;

namespace SaviumDrive.Helpers
{
    public static class ResourceHelper
    {
        // Instancia única del ResourceManager
        private static readonly ResourceManager _resourceManager = new ResourceManager();
        private static readonly ResourceMap _mainResourceMap = _resourceManager.MainResourceMap;

        public static string GetString(string key)
        {
            try
            {
                // 1. Intentar buscar en el subárbol estándar "Resources" (lo más común)
                var candidate = _mainResourceMap.TryGetValue($"Resources/{key}");

                // 2. Si falla, intentar buscar en la raíz directa
                if (candidate == null)
                {
                    candidate = _mainResourceMap.TryGetValue(key);
                }

                // 3. Si encontramos algo, devolver su valor limpio
                if (candidate != null)
                {
                    return candidate.ValueAsString.Replace("\\n", "\n");
                }
            }
            catch
            {
                // Silenciar cualquier error de búsqueda para no tumbar la app
            }

            // Fallback visual para saber qué falta
            return $"[{key}]";
        }

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