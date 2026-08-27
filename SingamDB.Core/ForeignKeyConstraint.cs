namespace SingamDB.Core;

public enum OnDeleteAction
{
    Restrict,
    Cascade,
    SetNull
}

public class ForeignKeyConstraint
{
    public string FieldName { get; }
    public string TargetCollectionName { get; }
    public string TargetFieldName { get; }
    public OnDeleteAction OnDelete { get; }

    public ForeignKeyConstraint(string fieldName, string targetCollectionName, string targetFieldName = "_id", OnDeleteAction onDelete = OnDeleteAction.Restrict)
    {
        FieldName = fieldName;
        TargetCollectionName = targetCollectionName;
        TargetFieldName = targetFieldName;
        OnDelete = onDelete;
    }
}
