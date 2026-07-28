using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Checks whether a principal (user) has a given access right on a Dataverse record, via
    /// <c>RetrievePrincipalAccessRequest</c>. Used by <see cref="PrepareImportHandler"/> to validate
    /// that the Custom API caller (<c>InitiatingUserId</c>) has Read access to the Import Template
    /// referenced by the job — see smelter-access-control, Requirement "Validación de acceso al
    /// template en PrepareImportPlugin".
    /// </summary>
    public interface IAccessChecker
    {
        /// <summary>Returns <c>true</c> when <paramref name="principalId"/> has Read access on <paramref name="target"/>.</summary>
        bool HasReadAccess(Guid principalId, EntityReference target);
    }

    /// <inheritdoc />
    public class PrincipalAccessChecker : IAccessChecker
    {
        /// <summary>Logical name of the Dataverse system user table, used to build the Principal reference.</summary>
        public const string SystemUserTable = "systemuser";

        private readonly IOrganizationService _service;

        /// <summary>
        /// Initializes a new instance of <see cref="PrincipalAccessChecker"/>.
        /// </summary>
        /// <param name="service">Organization service used to execute the RetrievePrincipalAccess message (SYSTEM context).</param>
        public PrincipalAccessChecker(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <inheritdoc />
        public bool HasReadAccess(Guid principalId, EntityReference target)
        {
            var response = (RetrievePrincipalAccessResponse)_service.Execute(new RetrievePrincipalAccessRequest
            {
                Principal = new EntityReference(SystemUserTable, principalId),
                Target = target
            });

            return (response.AccessRights & AccessRights.ReadAccess) == AccessRights.ReadAccess;
        }
    }
}
