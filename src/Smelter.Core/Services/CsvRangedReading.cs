using System;
using System.Collections.Generic;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>Fuente de bytes con acceso por rango (un File column de Dataverse, o un byte[] en tests).</summary>
    public interface IRangeSource
    {
        /// <summary>Tamaño total del archivo en bytes.</summary>
        long Length { get; }

        /// <summary>Lee hasta <paramref name="count"/> bytes desde <paramref name="offset"/> (menos si se acaba el archivo).</summary>
        byte[] Read(long offset, int count);
    }

    /// <summary>Implementación en memoria de <see cref="IRangeSource"/>.</summary>
    public sealed class ByteArrayRangeSource : IRangeSource
    {
        private readonly byte[] _bytes;
        public ByteArrayRangeSource(byte[] bytes) { _bytes = bytes ?? new byte[0]; }
        public long Length => _bytes.Length;

        public byte[] Read(long offset, int count)
        {
            if (offset >= _bytes.Length || count <= 0) return new byte[0];
            var n = (int)Math.Min(count, _bytes.Length - offset);
            var slice = new byte[n];
            Buffer.BlockCopy(_bytes, (int)offset, slice, 0, n);
            return slice;
        }
    }

    /// <summary>Resultado de leer la cabecera: nombres de columna y byte donde empieza el primer registro de datos.</summary>
    public sealed class CsvHeaderInfo
    {
        /// <summary>false cuando el archivo no se puede recorrer por bytes (UTF-16/32 o UTF-8 inválido): usar el camino legacy.</summary>
        public bool Supported { get; set; }

        public IReadOnlyList<string> Headers { get; set; } = new List<string>();

        /// <summary>Byte (absoluto, después del BOM y de la línea de cabecera) del primer registro de datos.</summary>
        public long DataStartOffset { get; set; }
    }

    /// <summary>Registros completos leídos desde un cursor de bytes y el cursor del siguiente registro sin leer.</summary>
    public sealed class CsvChunk
    {
        public IReadOnlyList<SourceRow> Rows { get; set; } = new List<SourceRow>();

        /// <summary>Byte absoluto del próximo registro sin consumir.</summary>
        public long NextOffset { get; set; }
    }
}
