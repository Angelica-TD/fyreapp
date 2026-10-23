using YamlDotNet.RepresentationModel;

namespace FyreApp.Services.Forms;

// One of a form's fields, as Uptick's form templates define them (dynamicformtemplateversions' YAML "config")
public sealed record FormField(
    string Key,
    string Type,
    string? Label,
    string? Initial,
    bool Required,
    // message: a heading and text shown between fields
    string? Heading,
    string? Text,
    // assertion: a question answered pass / fail (/ N/A), with what to ask when it fails
    string? Test,
    string? PassOption,
    string? FailOption,
    bool ShowNa,
    string? FailPrompt,
    // checkboxgroup: the choices
    IReadOnlyList<(string Value, string Label)> Options)
{
    public const string TextType = "text", Message = "message", Assertion = "assertion", Checkbox = "checkbox",
        CheckboxGroup = "checkboxgroup", Signature = "signature";

    // What's shown beside the input: the label, else the field's key made readable ("pass-partB" -> "Pass partB")
    public string Caption => Label ?? Test ?? Heading ?? Readable(Key);

    // Fields that take an answer (a message doesn't)
    public bool TakesValue => Type != Message;

    private static string Readable(string key)
    {
        var words = key.Replace('-', ' ').Replace('_', ' ').Trim();
        return words.Length == 0 ? key : char.ToUpperInvariant(words[0]) + words[1..];
    }
}

// The template's layout, when it has one: groups (Bootstrap rows and columns) holding text and fields
public sealed record FormLayoutNode(string Type, string? Class, string? Text, string? Field, IReadOnlyList<FormLayoutNode> Contents)
{
    public const string Group = "group", TextType = "text", FieldType = "field";
}

// A form template version's config: its fields in order, and its layout (if any)
public sealed class FormDefinition
{
    public IReadOnlyList<FormField> Fields { get; init; } = [];
    public IReadOnlyList<FormLayoutNode> Layout { get; init; } = [];

    public FormField? Field(string key) => Fields.FirstOrDefault(f => f.Key == key);

    // Fields the layout doesn't place, shown after it so nothing is lost
    public IReadOnlyList<FormField> Unplaced()
    {
        var placed = new HashSet<string>();
        void Walk(IEnumerable<FormLayoutNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.Field != null) placed.Add(n.Field);
                Walk(n.Contents);
            }
        }
        Walk(Layout);
        return Fields.Where(f => !placed.Contains(f.Key)).ToList();
    }

    public static FormDefinition Parse(string? config)
    {
        if (string.IsNullOrWhiteSpace(config)) return new FormDefinition();
        var yaml = new YamlStream();
        try
        {
            yaml.Load(new StringReader(config));
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return new FormDefinition();
        }
        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root) return new FormDefinition();

        var fields = new List<FormField>();
        if (Child(root, "fields") is YamlMappingNode fieldMap)
            foreach (var (k, v) in fieldMap.Children)
                if (k is YamlScalarNode { Value: { } key } && v is YamlMappingNode f)
                    fields.Add(FieldFrom(key.Trim(), f));

        return new FormDefinition
        {
            Fields = fields,
            Layout = Child(root, "layout") is YamlSequenceNode layout ? Nodes(layout) : []
        };
    }

    private static FormField FieldFrom(string key, YamlMappingNode f) => new(
        key,
        (Str(f, "type") ?? FormField.TextType).ToLowerInvariant(),
        Str(f, "label"),
        Str(f, "initial"),
        Bool(f, "required"),
        Str(f, "heading"),
        Str(f, "text") ?? Str(f, "body"),
        Str(f, "test"),
        Str(f, "pass_option"),
        Str(f, "fail_option"),
        Bool(f, "show_na"),
        Str(f, "fail_prompt"),
        Child(f, "options") is YamlSequenceNode options
            ? options.Children.OfType<YamlMappingNode>()
                .Select(o => (Value: Str(o, "value") ?? "", Label: Str(o, "label") ?? Str(o, "value") ?? ""))
                .Where(o => o.Value != "")
                .ToList()
            : []);

    private static IReadOnlyList<FormLayoutNode> Nodes(YamlSequenceNode seq) =>
        seq.Children.OfType<YamlMappingNode>()
            .Select(n => new FormLayoutNode(
                (Str(n, "type") ?? FormLayoutNode.Group).ToLowerInvariant(),
                Str(n, "class"),
                Str(n, "text"),
                Str(n, "field"),
                Child(n, "contents") is YamlSequenceNode c ? Nodes(c) : []))
            .ToList();

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.FirstOrDefault(c => c.Key is YamlScalarNode s && s.Value == key).Value;

    private static string? Str(YamlMappingNode map, string key) =>
        Child(map, key) is YamlScalarNode { Value: { } v } ? v.Trim() is { Length: > 0 } t ? t : null : null;

    private static bool Bool(YamlMappingNode map, string key) =>
        Str(map, key)?.ToLowerInvariant() is "true" or "yes";
}
