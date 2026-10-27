// Helpers compartidos por los smokes que llaman a las Custom APIs directo (ChunkSmoke, BenchRunner):
// retry del 400 transitorio de blob storage, búsqueda en la cadena de faults y subida multi-bloque.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class ApiCalls
{
    internal static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT: " + message);
    }

    internal static bool ChainContains(OrganizationServiceFault? fault, string needle)
    {
        for (; fault is not null; fault = fault.InnerFault)
        {
            if (fault.Message?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true) return true;
            if (fault.TraceText?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true) return true;
        }
        return false;
    }

    /// <summary>Mismo retry acotado que Program.cs: SOLO la firma transitoria 400 InvalidQueryParameterValue de blob storage.</summary>
    internal static OrganizationResponse ExecuteRetryingBlob(ServiceClient client, OrganizationRequest request)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            try { return client.Execute(request); }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> fx)
                when (attempt < maxAttempts && ChainContains(fx.Detail, "InvalidQueryParameterValue"))
            {
                Console.WriteLine($"      WARN: descarga del File transitoriamente rechazada (400 InvalidQueryParameterValue) — retry {attempt}/{maxAttempts - 1} en 15s ...");
                Thread.Sleep(TimeSpan.FromSeconds(15));
            }
        }
    }

    /// <summary>Sube un archivo a una columna File en bloques de BenchSpec.UploadBlockSize (UploadBlock acepta hasta 4 MB por bloque).</summary>
    internal static void UploadBlocks(ServiceClient client, EntityReference target, string fileAttribute, string fileName, byte[] content)
    {
        var token = (string)client.Execute(new OrganizationRequest("InitializeFileBlocksUpload")
        {
            ["Target"] = target,
            ["FileAttributeName"] = fileAttribute,
            ["FileName"] = fileName,
        })["FileContinuationToken"];

        var blockIds = new List<string>();
        foreach (var (offset, count) in BenchSpec.BlockRanges(content.Length, BenchSpec.UploadBlockSize))
        {
            var blockId = BenchSpec.BlockId(blockIds.Count);
            client.Execute(new OrganizationRequest("UploadBlock")
            {
                ["FileContinuationToken"] = token,
                ["BlockId"] = blockId,
                ["BlockData"] = content.AsSpan(offset, count).ToArray(),
            });
            blockIds.Add(blockId);
        }

        client.Execute(new OrganizationRequest("CommitFileBlocksUpload")
        {
            ["FileContinuationToken"] = token,
            ["FileName"] = fileName,
            ["MimeType"] = "text/csv",
            ["BlockList"] = blockIds.ToArray(),
        });
    }
}
