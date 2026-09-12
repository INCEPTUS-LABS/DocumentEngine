namespace Inceptus.DocumentEngine.Contracts.Commands;

public static class CommandValidationDiagnosticCodes
{
    public const string TargetDocumentMismatch = "CMD_TARGET_DOCUMENT_MISMATCH";

    public const string StaleRevision = "CMD_STALE_REVISION";

    public const string InvalidAffectedComponentDeclaration =
        "CMD_INVALID_AFFECTED_COMPONENT_DECLARATION";

    public const string UnsupportedCommandType = "CMD_UNSUPPORTED_TYPE";

    public const string DuplicateValidatorRegistration = "CMD_DUPLICATE_VALIDATOR_REGISTRATION";

    public const string ValidatorFailure = "CMD_VALIDATOR_FAILURE";
}
