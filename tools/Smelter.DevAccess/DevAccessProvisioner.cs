// Provisioning idempotente (y su inverso) del acceso de prueba en Dev, por SDK puro.
// La lógica de decisión vive en AccessPlanner (pura); acá solo hay I/O contra Dataverse.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;

namespace Smelter.DevAccess;

internal static class DevAccessProvisioner
{
    private const string TemplateTable = "sanic_tbl_smlt_importtemplate";
    private const string TableMappingTable = "sanic_tbl_smlt_tablemapping";
    private const string ColumnMappingTable = "sanic_tbl_smlt_columnmapping";
    private const string RelationshipMappingTable = "sanic_tbl_smlt_relationshipmapping";
    private const string JobTable = "sanic_tbl_smlt_importjob";

    // ── Lecturas ────────────────────────────────────────────────────────────────────────────

    private static List<RoleRef> RetrieveSmelterRoles(IOrganizationService svc) =>
        ToRoleRefs(svc.RetrieveMultiple(new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid", "name", "businessunitid"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.In, DevAccessSpec.SmelterRoleNames.Cast<object>().ToArray()) } },
        }).Entities);

    private static List<RoleRef> ToRoleRefs(IEnumerable<Entity> roles) =>
        roles.Select(r => new RoleRef(r.Id, r.GetAttributeValue<string>("name"),
            r.GetAttributeValue<EntityReference>("businessunitid")?.Id ?? Guid.Empty)).ToList();

    private static List<RoleRef> RetrieveUserSmelterRoles(IOrganizationService svc, Guid userId)
    {
        var q = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid", "name", "businessunitid"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.In, DevAccessSpec.SmelterRoleNames.Cast<object>().ToArray()) } },
        };
        var link = q.AddLink("systemuserroles", "roleid", "roleid");
        link.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
        return ToRoleRefs(svc.RetrieveMultiple(q).Entities);
    }

    private static Guid UserBusinessUnit(IOrganizationService svc, Guid userId) =>
        svc.Retrieve("systemuser", userId, new ColumnSet("businessunitid"))
            .GetAttributeValue<EntityReference>("businessunitid").Id;

    private static Entity? FindByName(IOrganizationService svc, string table, string nameAttr, string name, params string[] extraCols) =>
        svc.RetrieveMultiple(new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(extraCols),
            Criteria = { Conditions = { new ConditionExpression(nameAttr, ConditionOperator.Equal, name) } },
        }).Entities.FirstOrDefault();

    private static Entity? FindTeam(IOrganizationService svc, string name) =>
        svc.RetrieveMultiple(new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("teamid", "name", "businessunitid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, name),
                    new ConditionExpression("businessunitid", ConditionOperator.Equal, DevAccessSpec.BusinessUnitId),
                },
            },
        }).Entities.FirstOrDefault();

    /// <summary>Nombres de los teams QA de los que el usuario es miembro hoy.</summary>
    private static List<string> UserQaTeams(IOrganizationService svc, Guid userId, Dictionary<string, Guid> qaTeamIds)
    {
        if (qaTeamIds.Count == 0) return [];
        var memberships = svc.RetrieveMultiple(new QueryExpression("teammembership")
        {
            ColumnSet = new ColumnSet("teamid", "systemuserid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("systemuserid", ConditionOperator.Equal, userId),
                    new ConditionExpression("teamid", ConditionOperator.In, qaTeamIds.Values.Cast<object>().ToArray()),
                },
            },
        }).Entities.Select(m => m.GetAttributeValue<Guid>("teamid")).ToHashSet();
        return qaTeamIds.Where(kv => memberships.Contains(kv.Value)).Select(kv => kv.Key).ToList();
    }

    private static Dictionary<string, Guid> ExistingQaTeams(IOrganizationService svc)
    {
        var map = new Dictionary<string, Guid>();
        foreach (var name in DevAccessSpec.QaTeamNames)
            if (FindTeam(svc, name) is { } t) map[name] = t.Id;
        return map;
    }

    // ── Escrituras ──────────────────────────────────────────────────────────────────────────

    private static void AssociateRole(IOrganizationService svc, string entity, Guid targetId, string relationship, Guid roleId) =>
        svc.Execute(new AssociateRequest
        {
            Target = new EntityReference(entity, targetId),
            Relationship = new Relationship(relationship),
            RelatedEntities = new EntityReferenceCollection { new EntityReference("role", roleId) },
        });

    private static void DisassociateRole(IOrganizationService svc, string entity, Guid targetId, string relationship, Guid roleId) =>
        svc.Execute(new DisassociateRequest
        {
            Target = new EntityReference(entity, targetId),
            Relationship = new Relationship(relationship),
            RelatedEntities = new EntityReferenceCollection { new EntityReference("role", roleId) },
        });

    private static Guid EnsureTeam(IOrganizationService svc, string name, List<RoleRef> allRoles)
    {
        var team = FindTeam(svc, name);
        Guid teamId;
        if (team is null)
        {
            teamId = svc.Create(new Entity("team")
            {
                ["name"] = name,
                ["businessunitid"] = new EntityReference("businessunit", DevAccessSpec.BusinessUnitId),
                ["teamtype"] = new OptionSetValue(0), // Owner
            });
            Console.WriteLine($"[team]   creado '{name}' ({teamId}).");
        }
        else
        {
            teamId = team.Id;
            Console.WriteLine($"[team]   ya existe '{name}' ({teamId}).");
        }

        // El team necesita un rol para poder ser owner (Dataverse rechaza el assign sin rol).
        var teamRole = AccessPlanner.PickRoleForBu(allRoles, DevAccessSpec.TeamRoleName, DevAccessSpec.BusinessUnitId)
            ?? throw new InvalidOperationException($"No existe la copia del rol '{DevAccessSpec.TeamRoleName}' en la BU {DevAccessSpec.BusinessUnitId}.");
        var teamRoleIds = svc.RetrieveMultiple(new QueryExpression("teamroles")
        {
            ColumnSet = new ColumnSet("roleid"),
            Criteria = { Conditions = { new ConditionExpression("teamid", ConditionOperator.Equal, teamId) } },
        }).Entities.Select(e => e.GetAttributeValue<Guid>("roleid")).ToHashSet();
        if (!teamRoleIds.Contains(teamRole.Id))
        {
            AssociateRole(svc, "team", teamId, "teamroles_association", teamRole.Id);
            Console.WriteLine($"[team]   rol '{teamRole.Name}' asociado a '{name}'.");
        }
        return teamId;
    }

    private static Guid EnsureTemplate(ServiceClient svc, QaTemplateSpec spec, Guid teamId)
    {
        var existing = FindByName(svc, TemplateTable, "sanic_smlt_it_name", spec.Name, "sanic_smlt_it_status", "ownerid");
        Guid templateId;
        if (existing is null)
        {
            var json = new TemplateSerializer().Serialize(BuildTemplateDto(spec.Name));
            var resp = svc.Execute(new OrganizationRequest(DevAccessSpec.ImportTemplateApi) { ["Json"] = json });
            templateId = (Guid)resp["TemplateId"];
            Console.WriteLine($"[tpl]    creado '{spec.Name}' ({templateId}).");
        }
        else
        {
            templateId = existing.Id;
            Console.WriteLine($"[tpl]    ya existe '{spec.Name}' ({templateId}).");
        }

        var current = svc.Retrieve(TemplateTable, templateId, new ColumnSet("sanic_smlt_it_status", "ownerid"));
        if (current.GetAttributeValue<OptionSetValue>("sanic_smlt_it_status")?.Value != (int)ImportTemplateStatus.Active)
        {
            svc.Update(new Entity(TemplateTable, templateId)
            {
                ["sanic_smlt_it_status"] = new OptionSetValue((int)ImportTemplateStatus.Active),
            });
            Console.WriteLine($"[tpl]    '{spec.Name}' -> Active.");
        }

        var owner = current.GetAttributeValue<EntityReference>("ownerid");
        if (owner is null || owner.Id != teamId)
        {
            svc.Execute(new AssignRequest
            {
                Assignee = new EntityReference("team", teamId),
                Target = new EntityReference(TemplateTable, templateId),
            });
            Console.WriteLine($"[tpl]    '{spec.Name}' asignado a '{spec.OwnerTeamName}'.");
        }
        return templateId;
    }

    /// <summary>Mismo shape que el smoke de Smelter.Verify: 1 table mapping a contact, dedup en emailaddress1.</summary>
    private static TemplateExportDto BuildTemplateDto(string name) => new()
    {
        FormatVersion = "1.0",
        Template = new TemplateInfoDto { Name = name, SourceType = 0, HasHeaderRow = true, Delimiter = ",", ChunkSize = 0 },
        TableMappings =
        [
            new TableMappingDto
            {
                Ref = "tm-contact", Name = "Contact (QA)", TargetTable = "contact",
                ProcessingOrder = 1, DedupColumns = "emailaddress1",
                CreateIfNotExists = true, UpdateIfExists = true,
                ColumnMappings =
                [
                    new ColumnMappingDto { Name = "Email", SourceColumn = "Email", TargetColumn = "emailaddress1", DataType = (int)ColumnDataType.String },
                    new ColumnMappingDto { Name = "FirstName", SourceColumn = "FirstName", TargetColumn = "firstname", DataType = (int)ColumnDataType.String },
                    new ColumnMappingDto { Name = "LastName", SourceColumn = "LastName", TargetColumn = "lastname", DataType = (int)ColumnDataType.String },
                ],
            },
        ],
    };

    // ── Provision ───────────────────────────────────────────────────────────────────────────

    internal static void Provision(ServiceClient svc)
    {
        var allRoles = RetrieveSmelterRoles(svc);

        Console.WriteLine("[1] Teams QA ...");
        var teamIds = DevAccessSpec.QaTeamNames.ToDictionary(n => n, n => EnsureTeam(svc, n, allRoles));

        Console.WriteLine("[2] Roles y membresías de los usuarios de test ...");
        foreach (var u in DevAccessSpec.Users)
        {
            var buId = UserBusinessUnit(svc, u.UserId);
            var desiredRole = u.RoleName is null ? null : AccessPlanner.PickRoleForBu(allRoles, u.RoleName, buId)
                ?? throw new InvalidOperationException($"{u.Label}: no existe la copia de '{u.RoleName}' en la BU del usuario ({buId}).");
            var roleDiff = AccessPlanner.DiffRoles(desiredRole, RetrieveUserSmelterRoles(svc, u.UserId));
            foreach (var r in roleDiff.ToRemove)
            {
                DisassociateRole(svc, "systemuser", u.UserId, "systemuserroles_association", r.Id);
                Console.WriteLine($"[user]   {u.Label}: rol '{r.Name}' ({r.Id}) quitado.");
            }
            foreach (var r in roleDiff.ToAdd)
            {
                AssociateRole(svc, "systemuser", u.UserId, "systemuserroles_association", r.Id);
                Console.WriteLine($"[user]   {u.Label}: rol '{r.Name}' ({r.Id}) asociado.");
            }

            var memberDiff = AccessPlanner.DiffMembership(u.TeamName, UserQaTeams(svc, u.UserId, teamIds));
            foreach (var t in memberDiff.ToRemove)
            {
                svc.Execute(new RemoveMembersTeamRequest { TeamId = teamIds[t], MemberIds = [u.UserId] });
                Console.WriteLine($"[user]   {u.Label}: sacado de '{t}'.");
            }
            foreach (var t in memberDiff.ToAdd)
            {
                svc.Execute(new AddMembersTeamRequest { TeamId = teamIds[t], MemberIds = [u.UserId] });
                Console.WriteLine($"[user]   {u.Label}: agregado a '{t}'.");
            }
        }

        Console.WriteLine("[3] Templates QA ...");
        foreach (var t in DevAccessSpec.Templates)
            EnsureTemplate(svc, t, teamIds[t.OwnerTeamName]);

        PrintMatrix(svc);
    }

    // ── Remove ──────────────────────────────────────────────────────────────────────────────

    internal static void Remove(ServiceClient svc)
    {
        Console.WriteLine("[1] Roles Smelter de los usuarios de test ...");
        foreach (var u in DevAccessSpec.Users)
            foreach (var r in AccessPlanner.DiffRoles(null, RetrieveUserSmelterRoles(svc, u.UserId)).ToRemove)
            {
                DisassociateRole(svc, "systemuser", u.UserId, "systemuserroles_association", r.Id);
                Console.WriteLine($"[user]   {u.Label}: rol '{r.Name}' quitado.");
            }

        Console.WriteLine("[2] Templates QA (con jobs y mappings: la relación es RemoveLink, no cascade delete) ...");
        foreach (var t in DevAccessSpec.Templates)
        {
            var tpl = FindByName(svc, TemplateTable, "sanic_smlt_it_name", t.Name);
            if (tpl is null) { Console.WriteLine($"[tpl]    '{t.Name}' no existe."); continue; }

            DeleteWhere(svc, JobTable, "sanic_smlt_ij_importtemplateid", tpl.Id);
            var tableMappings = svc.RetrieveMultiple(new QueryExpression(TableMappingTable)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = { Conditions = { new ConditionExpression("sanic_smlt_tm_importtemplateid", ConditionOperator.Equal, tpl.Id) } },
            }).Entities;
            foreach (var tm in tableMappings)
            {
                DeleteWhere(svc, ColumnMappingTable, "sanic_smlt_cm_tablemappingid", tm.Id);
                DeleteWhere(svc, RelationshipMappingTable, "sanic_smlt_rm_parenttablemappingid", tm.Id);
                DeleteWhere(svc, RelationshipMappingTable, "sanic_smlt_rm_childtablemappingid", tm.Id);
                svc.Delete(TableMappingTable, tm.Id);
            }
            svc.Delete(TemplateTable, tpl.Id);
            Console.WriteLine($"[tpl]    '{t.Name}' eliminado.");
        }

        Console.WriteLine("[3] Teams QA ...");
        foreach (var name in DevAccessSpec.QaTeamNames)
        {
            var team = FindTeam(svc, name);
            if (team is null) { Console.WriteLine($"[team]   '{name}' no existe."); continue; }
            svc.Delete("team", team.Id);
            Console.WriteLine($"[team]   '{name}' eliminado.");
        }

        PrintMatrix(svc);
    }

    private static void DeleteWhere(IOrganizationService svc, string table, string lookupAttr, Guid parentId)
    {
        foreach (var e in svc.RetrieveMultiple(new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression(lookupAttr, ConditionOperator.Equal, parentId) } },
        }).Entities)
            svc.Delete(table, e.Id);
    }

    // ── Matriz final (sin secretos) ─────────────────────────────────────────────────────────

    internal static void PrintMatrix(ServiceClient svc)
    {
        Console.WriteLine();
        Console.WriteLine("=== Matriz de acceso (Dev) ===");
        var teamIds = ExistingQaTeams(svc);
        foreach (var u in DevAccessSpec.Users)
        {
            var roles = RetrieveUserSmelterRoles(svc, u.UserId).Select(r => r.Name).OrderBy(n => n).ToList();
            var teams = UserQaTeams(svc, u.UserId, teamIds);
            Console.WriteLine($"{u.Label} ({u.UserId})  roles=[{(roles.Count == 0 ? "-" : string.Join(", ", roles))}]  teams=[{(teams.Count == 0 ? "-" : string.Join(", ", teams))}]");
        }
        foreach (var t in DevAccessSpec.Templates)
        {
            var tpl = FindByName(svc, TemplateTable, "sanic_smlt_it_name", t.Name, "ownerid", "sanic_smlt_it_status");
            if (tpl is null) { Console.WriteLine($"template '{t.Name}': (no existe)"); continue; }
            var owner = tpl.GetAttributeValue<EntityReference>("ownerid");
            var status = tpl.GetAttributeValue<OptionSetValue>("sanic_smlt_it_status")?.Value;
            Console.WriteLine($"template '{t.Name}': owner={owner?.LogicalName}:{owner?.Name ?? owner?.Id.ToString()} status={(status == (int)ImportTemplateStatus.Active ? "Active" : status?.ToString())}");
        }
    }
}
