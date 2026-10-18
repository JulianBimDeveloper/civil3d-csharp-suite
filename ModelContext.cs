using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace XTS_Evolution_PB
{
    public static class ModelContext
    {
        public static string CurrentPhase { get; set; } = "MEA";

        public static readonly Dictionary<string, string> Phases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "MEA", "Estado actual / A mantener" },
            { "MDE", "Demoliciones" },
            { "MPR", "Proyecto" },
            { "MOI", "Inicio de Obra" },
            { "MOM", "Seguimiento de Obra" },
            { "MRF", "As-Built / Final de Obra" },
            { "MMA", "Operación y Mantenimiento (O&M)" },
            { "MMP", "Maqueta de prueba" },
            { "MCC", "Coordenadas compartidas" },
            { "MAM", "A mantener" },
            { "MAD", "A demoler" },
            { "MNU", "Modelos Nuevos" },
            { "MTE", "Elementos Temporales" }
        };

        /// <summary>
        /// Intenta detectar la fase a partir del nombre del archivo.
        /// Busca patrones como _MEA_, _MOI_, etc. o que el archivo termine en el acrónimo.
        /// </summary>
        public static string DetectPhaseFromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "MEA";

            string name = Path.GetFileNameWithoutExtension(fileName).ToUpper();
            
            foreach (var acronym in Phases.Keys)
            {
                // Buscamos el acrónimo rodeado de guiones o al principio/final
                if (name.Contains($"_{acronym}_") || 
                    name.EndsWith($"_{acronym}") || 
                    name.StartsWith($"{acronym}_") ||
                    name == acronym)
                {
                    return acronym;
                }
            }

            return "MEA"; // Default si no encuentra nada
        }

        public static string GetPhaseDescription(string acronym)
        {
            if (Phases.TryGetValue(acronym, out string desc)) return desc;
            return "Fase desconocida";
        }
    }
}
