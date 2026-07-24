using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Downloads a Dataverse file column via the InitializeFileBlocksDownload / DownloadBlock messages.
    /// Extracted from the legacy plugin so it can be unit-tested and reused by PrepareImport.
    /// </summary>
    public interface IFileDownloader
    {
        byte[] Download(EntityReference target, string fileAttributeName);
    }

    /// <inheritdoc />
    public class FileDownloader : IFileDownloader
    {
        /// <summary>
        /// Maximum bytes fetched per <c>DownloadBlock</c> call. 4 MB is the Dataverse-recommended
        /// block size for file column streaming — larger blocks increase memory pressure inside the
        /// plugin sandbox without a meaningful throughput gain.
        /// </summary>
        private const int BlockSize = 4 * 1024 * 1024; // 4 MB

        private readonly IOrganizationService _service;

        /// <summary>
        /// Initializes a new instance of <see cref="FileDownloader"/>.
        /// </summary>
        /// <param name="service">Organization service used to execute file-block messages (SYSTEM context).</param>
        public FileDownloader(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Downloads the full content of a Dataverse file column in streaming blocks, reassembling
        /// them into a single byte array. Uses the two-stage
        /// <c>InitializeFileBlocksDownload</c> → <c>DownloadBlock</c> Dataverse message pair.
        /// </summary>
        /// <param name="target">Reference to the record that owns the file column.</param>
        /// <param name="fileAttributeName">Logical name of the file-type column.</param>
        /// <returns>Complete file content as a byte array.</returns>
        public byte[] Download(EntityReference target, string fileAttributeName)
        {
            var init = _service.Execute(new OrganizationRequest("InitializeFileBlocksDownload")
            {
                ["Target"] = target,
                ["FileAttributeName"] = fileAttributeName
            });

            var token = (string)init["FileContinuationToken"];
            var size = (long)init["FileSizeInBytes"];

            var bytes = new List<byte>();
            long offset = 0;
            while (offset < size)
            {
                var download = _service.Execute(new OrganizationRequest("DownloadBlock")
                {
                    ["Offset"] = offset,
                    ["BlockLength"] = (long)Math.Min(BlockSize, size - offset),
                    ["FileContinuationToken"] = token
                });

                var block = (byte[])download["Data"];
                bytes.AddRange(block);
                offset += block.Length;
            }

            return bytes.ToArray();
        }
    }
}
