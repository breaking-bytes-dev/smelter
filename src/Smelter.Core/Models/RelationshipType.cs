namespace Smelter.Plugins.Models
{
    /// <summary>
    /// Maps to the OptionSet values on <c>sanic_smlt_rm_relationshiptype</c>. ManyToMany (1) fue eliminado del
    /// choice en 1.0.10: nunca estuvo implementado y el motor lo ignoraba en silencio.
    /// </summary>
    public enum RelationshipType
    {
        /// <summary>Child table holds a lookup to the parent (1:N). The only supported type.</summary>
        OneToMany = 0
    }
}
