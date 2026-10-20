using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// An Uptick security group (Uptick "groups"), e.g. "Technician - Higher Permissions". Users export
// "Security Groups" joins a user's group names with " - ", which these names let FyreApp split again.
public class SecurityGroup : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // Maps from the groups export "id"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
}

// A custom field set up in Uptick (Uptick "extrafields"), e.g. "ASE Connected" on assets. A record's values are
// in its Uptick record's "extra_fields" (by Name); these say which are extra fields, in what order, and how
// they're entered.
public class ExtraField : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // Maps from the extra fields export "id"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // Uptick's record type number ("content_type"); see RecordType
    [StringLength(20)]
    public string? ContentType { get; set; }

    public int Order { get; set; }

    // As the record's export names the column, e.g. "Mains Pressure (kPa)"
    [Required, StringLength(200)]
    public string Label { get; set; } = string.Empty;

    // e.g. "mains_pressure_kpa"
    [StringLength(200)]
    public string? Name { get; set; }

    // text, choice, integer, date
    [StringLength(50)]
    public string? Widget { get; set; }

    [StringLength(500)]
    public string? Default { get; set; }

    public bool IsRequired { get; set; }

    [StringLength(1000)]
    public string? HelpText { get; set; }

    public string? FieldSettings { get; set; }

    // Uptick's record type numbers, worked out from which export has the field as a column
    public const string AssetContentType = "51";
    public const string PropertyContentType = "19";
    public const string UserContentType = "5";

    public string RecordType => ContentType switch
    {
        AssetContentType => "Asset",
        PropertyContentType => "Property",
        UserContentType => "User",
        null or "" => "-",
        var other => $"Other ({other})"
    };
}

// An Uptick inspection prompt question (Uptick "promptquestions"): a question in a promptset that technicians
// answer on site
public class PromptQuestion : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // Maps from the prompt questions export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    [StringLength(200)]
    public string? PromptsetName { get; set; }

    [StringLength(100)]
    public string? PromptsetRef { get; set; }

    [StringLength(100)]
    public string? PromptsetType { get; set; }

    // e.g. a choice, text or number question
    [StringLength(100)]
    public string? Type { get; set; }

    [Required, StringLength(500)]
    public string Label { get; set; } = string.Empty;

    public string? HelpText { get; set; }

    [StringLength(100)]
    public string? Ref { get; set; }

    public bool Active { get; set; } = true;
    public bool Required { get; set; }
    public int? Order { get; set; }
}
