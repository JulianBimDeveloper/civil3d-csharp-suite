using System.Collections.Generic;
using System.Linq;

namespace XTS_Evolution_PB.IFC
{
    public static class ProjectParamsMap
    {
        public readonly record struct Entry(string CustomPropName, string PsetName, string PropertyName);

        // Espejo de los Psets marcados como ApplicableEntities="IfcProject" en
        // REFERENCIA/IfcInfraExportPropertyMapping.json. Los nombres con espacio trailing
        // son intencionales y respetan el JSON ADIF original.
        public static readonly Entry[] Entries = new[]
        {
            new Entry("ADIF_00_Codigo_Archivo",       "ADIF_00_Generales",    "ADIF_00_Codigo_Archivo "),

            new Entry("ADIF_02_Codigo_Actuacion",     "ADIF_02_Localizacion", "ADIF_02_Codigo_Actuacion "),
            new Entry("ADIF_02_Direccion_Ubicacion",  "ADIF_02_Localizacion", "ADIF_02_Direccion_Ubicacion "),
            new Entry("ADIF_02_Entorno_Actuacion",    "ADIF_02_Localizacion", "ADIF_02_Entorno_Actuacion "),
            new Entry("ADIF_02_Expediente_Actuacion", "ADIF_02_Localizacion", "ADIF_02_Expediente_Actuacion "),
            new Entry("ADIF_02_Nombre_Actuacion",     "ADIF_02_Localizacion", "ADIF_02_Nombre_Actuacion "),
            new Entry("ADIF_02_Nombre_Global",        "ADIF_02_Localizacion", "ADIF_02_Nombre_Global "),
            new Entry("ADIF_02_Sistema_Coordenadas",  "ADIF_02_Localizacion", "ADIF_02_Sistema_Coordenadas "),

            new Entry("ADIF_04_Inicio_Actuacion",     "ADIF_04_4D",           "ADIF_04_Inicio_Actuacion "),
            new Entry("ADIF_04_Fin_Actuacion",        "ADIF_04_4D",           "ADIF_04_Fin_Actuacion "),
        };

        public static IEnumerable<IGrouping<string, Entry>> GroupedByPset()
            => Entries.GroupBy(e => e.PsetName);
    }
}
