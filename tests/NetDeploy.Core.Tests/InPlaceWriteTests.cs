using NetDeploy.Core.Config;

namespace NetDeploy.Core.Tests;

/// <summary>Settings are written into the file's own text, so everything else in it stays as it was written.</summary>
public class InPlaceWriteTests
{
    private static readonly IConfigFormat Json = ConfigFormats.For("appsettings.json");
    private static readonly IConfigFormat Xml = ConfigFormats.For("Orders.exe.config");

    private const string Settings = """
        {
            // Where orders go
            "ConnectionStrings": { "Orders": "Server=sql01" },
            "Smtp": {
                "Host": "mail",
                "Port": 25
            }
        }
        """;

    private static string Set(IConfigFormat format, string text, string path, string? value) =>
        format.Write(text, [KeyValuePair.Create(path, value)]);

    [Fact]
    public void A_json_value_is_swapped_where_it_is_written()
    {
        Assert.Equal(Settings.Replace("25", "587"), Set(Json, Settings, "Smtp:Port", "587"));
        Assert.Equal(Settings.Replace("sql01", "sql02"), Set(Json, Settings, "ConnectionStrings:Orders", "\"Server=sql02\""));
    }

    [Fact]
    public void A_new_json_setting_goes_last_in_its_section_with_the_same_indentation()
    {
        Assert.Equal(Settings.Replace("\"Port\": 25", "\"Port\": 25,\n        \"Ssl\": true"), Set(Json, Settings, "Smtp:Ssl", "true"));
        Assert.Equal(Settings.Replace("\"Server=sql01\" }", "\"Server=sql01\", \"Reports\": \"Server=sql03\" }"),
            Set(Json, Settings, "ConnectionStrings:Reports", "\"Server=sql03\""));
        Assert.Equal(Settings.Replace("    }\n}", "    },\n    \"Alerts\": {\n        \"Emails\": [\"ops\"]\n    }\n}"),
            Set(Json, Settings, "Alerts:Emails", """["ops"]"""));
    }

    [Fact]
    public void A_json_setting_taken_out_goes_with_its_comma()
    {
        Assert.Equal(Settings.Replace("\"Host\": \"mail\",\n        ", ""), Set(Json, Settings, "Smtp:Host", null));
        Assert.Equal(Settings.Replace(",\n        \"Port\": 25", ""), Set(Json, Settings, "Smtp:Port", null));
        Assert.Equal("""{ "b": 2 }""", Set(Json, """{ "a": 1, "b": 2 }""", "a", null));
        Assert.Equal("""{ "a": 1 }""", Set(Json, """{ "a": 1, "b": 2 }""", "b", null));
    }

    [Fact]
    public void A_value_a_source_file_has_is_written_the_way_that_file_writes_it()
    {
        const string server = "{\n  \"Hosts\": [\n    \"a\",\n    \"b\"\n  ]\n}";
        var written = Json.Write("""{ "Hosts": [] }""", [KeyValuePair.Create("Hosts", (string?)"""["a","b"]""")], [server]);

        Assert.Equal("{ \"Hosts\": [\n    \"a\",\n    \"b\"\n  ] }", written);
    }

    [Fact]
    public void Locate_finds_each_settings_value()
    {
        var spans = Json.Locate(Settings);

        Assert.Equal("25", Settings[spans["smtp:port"].Start..spans["smtp:port"].End]);
        Assert.Equal("\"Server=sql01\"", Settings[spans["ConnectionStrings:Orders"].Start..spans["ConnectionStrings:Orders"].End]);
    }

    private const string Config = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <startup><supportedRuntime version="v4.0"/></startup>
          <appSettings>
            <add key="Mode" value="Live"/>
            <add key='Region' value='East'/>
          </appSettings>
        </configuration>
        """;

    [Fact]
    public void An_xml_element_is_swapped_and_the_rest_keeps_its_quotes_and_spacing()
    {
        Assert.Equal(Config.Replace("<add key='Region' value='East'/>", "<add key=\"Region\" value=\"West\" />"),
            Set(Xml, Config, "appSettings:Region", "<add key=\"Region\" value=\"West\" />"));
    }

    [Fact]
    public void A_new_xml_element_goes_after_the_last_and_one_taken_out_goes_with_its_line()
    {
        Assert.Equal(Config.Replace("value='East'/>", "value='East'/>\n    <add key=\"Batch\" value=\"5\" />"),
            Set(Xml, Config, "appSettings:Batch", "<add key=\"Batch\" value=\"5\" />"));
        Assert.Equal(Config.Replace("    <add key=\"Mode\" value=\"Live\"/>\n", ""), Set(Xml, Config, "appSettings:Mode", null));
        Assert.Equal(Config.Replace("  </appSettings>\n", "  </appSettings>\n  <connectionStrings>\n    <add name=\"Db\" connectionString=\"x\" />\n  </connectionStrings>\n"),
            Set(Xml, Config, "connectionStrings:Db", "<add name=\"Db\" connectionString=\"x\" />"));
    }

    [Fact]
    public void Any_other_text_file_is_one_value()
    {
        var plain = ConfigFormats.For("readme.txt");

        Assert.Equal("hello", Assert.Single(plain.Read("hello")).Value);
        Assert.Equal("bye", Set(plain, "hello", "", "bye"));
        Assert.Equal((0, 5), plain.Locate("hello")[""]);
    }
}
