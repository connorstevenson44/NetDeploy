using NetDeploy.Core.Config;

namespace NetDeploy.Core.Tests;

public class XmlMergeTests
{
    private static readonly IConfigFormat Xml = ConfigFormats.For("Orders.exe.config");

    private const string Current = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <appSettings>
            <add key="Mode" value="Live" />
            <add key="Legacy" value="1" />
          </appSettings>
          <connectionStrings>
            <add name="Orders" connectionString="Server=prod" providerName="System.Data.SqlClient" />
          </connectionStrings>
          <runtime><old /></runtime>
        </configuration>
        """;

    private const string Incoming = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <!-- keep this comment -->
          <appSettings>
            <add key="Mode" value="Test" />
            <add key="Retries" value="3" />
          </appSettings>
          <connectionStrings>
            <add name="Orders" connectionString="Server=localhost" providerName="System.Data.SqlClient" />
          </connectionStrings>
          <runtime><new /></runtime>
        </configuration>
        """;

    [Fact]
    public void AppSettings_and_connection_strings_merge_by_key_and_other_sections_come_from_the_package()
    {
        var plan = ConfigMerger.Plan(null, Current, Incoming, Xml, new MergeOptions([]));

        Assert.Equal(MergeChoice.KeepCurrent, plan.Rows.Single(r => r.Path == "appSettings:Mode").Choice);
        Assert.Equal(MergeChoice.TakeIncoming, plan.Rows.Single(r => r.Path == "appSettings:Retries").Choice);
        Assert.Equal(MergeChoice.Remove, plan.Rows.Single(r => r.Path == "appSettings:Legacy").Choice);
        Assert.Equal(MergeChoice.KeepCurrent, plan.Rows.Single(r => r.Path == "connectionStrings:Orders").Choice);

        var merged = plan.BuildResult();
        Assert.Matches(@"^<\?xml[^>]*\?>\r?\n<configuration>", merged);
        Assert.Contains("<!-- keep this comment -->", merged);
        Assert.Contains("""value="Live" """, merged);
        Assert.Contains("""key="Retries" """, merged);
        Assert.DoesNotContain("Legacy", merged);
        Assert.Contains("Server=prod", merged);
        Assert.Contains("<new />", merged);
        Assert.DoesNotContain("<old />", merged);
    }

    [Fact]
    public void Display_and_edit_work_on_the_value_attribute()
    {
        var connection = Xml.Read(Current)["connectionStrings:Orders"];

        Assert.Equal("Server=prod", Xml.Display(connection));

        var edited = Xml.ParseEdit(connection, "Server=new");
        Assert.Equal("Server=new", Xml.Display(edited));
        Assert.Contains("providerName=\"System.Data.SqlClient\"", edited);
    }

    [Fact]
    public void A_kept_setting_missing_from_the_package_is_added_back_whole()
    {
        var plan = ConfigMerger.Plan(null, Current, Incoming, Xml, new MergeOptions([]));
        plan.Rows.Single(row => row.Path == "appSettings:Legacy").Choice = MergeChoice.KeepCurrent;

        Assert.Contains("""<add key="Legacy" value="1" />""", plan.BuildResult());
    }
}
