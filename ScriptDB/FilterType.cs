namespace ScriptDb
{
    public enum FilterType
    {
        Table,
        TableData,
        View,
        StoredProcedure,
        Default,
        Assembly,
        AuditSpecification,
        // encryption key -- only one, so no filtering needed
        ExtendedProperty,
        ExtendedStoredProcedure,
        SearchPropertyList,
        Sequence,
        Synonym,
        SymmetricKey,
        DdlTrigger, // db.Triggers collection
        UserDefinedAggregate,
        UserDefinedDataType,
        UserDefinedFunction,
        UserDefinedTableType,
        UserDefinedType,
        User,
        Role,
        Rule,
        Schema,
        XmlSchema,
    }
}
