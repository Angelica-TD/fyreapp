namespace FyreApp.Services.Techs;

public enum TechCreateStatus { Success, EmailAlreadyExists, Failed }
public record TechCreateResult(TechCreateStatus Status, string? TechId = null, IEnumerable<string>? Errors = null);

public enum TechUpdateStatus { Success, NotFound, EmailAlreadyExists, Failed }
public record TechUpdateResult(TechUpdateStatus Status, IEnumerable<string>? Errors = null);

public enum TechDeactivateStatus { Success, NotFound, Failed }
public record TechDeactivateResult(TechDeactivateStatus Status);

public enum TechDeleteStatus { Success, NotFound, Failed }
public record TechDeleteResult(TechDeleteStatus Status);