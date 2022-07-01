// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;

var configuration = new ConfigurationBuilder()
     .SetBasePath(Directory.GetCurrentDirectory())
     .AddJsonFile($"appsettings.json");

var config = configuration.Build();
string connectionString = config.GetConnectionString("DataConnection");

var tables = ListTables();

foreach (var table in tables)
{
    //Console.WriteLine($"\nTable Name: {table}\n");
    await WorkOnTableDesign(table);
}

async Task WorkOnTableDesign(string tableName)
{
    var sql = @"
	SELECT COLUMNDATA.COLUMN_NAME AS Name,
    DATA_TYPE AS Type,
    CHARACTER_MAXIMUM_LENGTH AS MaxLength,
    COLUMN_DEFAULT AS DefaultValue,
    IS_NULLABLE As IsNullable,
    CASE WHEN TABLECONSTRAINTS.CONSTRAINT_TYPE = 'PRIMARY KEY' THEN 1 ELSE 0 END AS 'Primary'
    FROM INFORMATION_SCHEMA.COLUMNS COLUMNDATA
    LEFT OUTER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE COLUMNUSAGE 
    ON COLUMNUSAGE.COLUMN_NAME = COLUMNDATA.COLUMN_NAME 
    AND COLUMNUSAGE.TABLE_NAME = COLUMNDATA.TABLE_NAME
    LEFT OUTER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS TABLECONSTRAINTS 
    ON TABLECONSTRAINTS.CONSTRAINT_TYPE = 'PRIMARY KEY'
    AND TABLECONSTRAINTS.CONSTRAINT_NAME = COLUMNUSAGE.CONSTRAINT_NAME
    WHERE COLUMNDATA.TABLE_NAME = N'" + tableName + "'";

    using SqlConnection connection = new(connectionString);

    connection.Open();

    SqlCommand command = new(sql, connection);

    var fieldData = new List<FieldData>();

    try
    {
        SqlDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            var fieldName = reader[0].ToString();

            var fieldType = reader[1].ToString();

            _ = int.TryParse(reader[2].ToString() ?? "0", out int fieldLength);

            var hasDefaultValue = !string.IsNullOrEmpty(reader[3].ToString());

            var isNullable = reader[4].ToString() == "YES";

            var isPrimary = int.Parse(reader[5].ToString() ?? "0") == 1;

            //Console.WriteLine($"\nField Name: {fieldName} - Field Type: {fieldType} - Field Length: {fieldLength} - Field IsNullable: {isNullable}\n");

            if (isPrimary || hasDefaultValue) continue;

            fieldData.Add(new FieldData
            {
                Name = fieldName,
                Type = fieldType,
                Length = fieldLength,
                IsPrimary = isPrimary,
                IsNullable = isNullable
            });
        }

        reader.Close();
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);
    }

    connection.Close();
    connection.Dispose();

    await CreateValidator(tableName, fieldData);
}

IEnumerable<string> ListTables()
{
    List<string> tables = new();

    using (SqlConnection connection = new(connectionString))
    {
        connection.Open();

        DataTable dt = connection.GetSchema("Tables");

        foreach (DataRow row in dt.Rows)
        {
            string tablename = (string)row[2];
            if (tablename.Contains('_')) continue; //For skip backup tables
            tables.Add(tablename);
        }

        connection.Close();
        connection.Dispose();
    }

    return tables;
}

async Task CreateValidator(string tableName, List<FieldData> fieldData)
{
    var data = "";

    foreach (var item in fieldData)
    {
        if (!item.IsNullable && item.Length > 0)
        {
            data += "RuleFor(u => u." + item.Name + ").Cascade(CascadeMode.StopOnFirstFailure).NotEmpty().WithMessage(\"{PropertyName} is required\")\r\n"
                   + ".Length(1, " + item.Length + ").WithMessage(\"{PropertyName} Length of {TotalLength} is invalid. A maximum of "
                   + item.Length + " characters is allowed\");";

            data += "\r\n";

        }
        else
        {
            if (!item.IsNullable)
            {
                data += "RuleFor(u => u." + item.Name + ").Cascade(CascadeMode.StopOnFirstFailure).NotEmpty().WithMessage(\"{PropertyName} is required\");";
                data += "\r\n";
            }

            if (item.Length > 0)
            {
                data += @"RuleFor(u => u." + item.Name + ").Cascade(CascadeMode.StopOnFirstFailure)" +
                   ".MaximumLength(" + item.Length + ").WithMessage(\"{PropertyName} Length of {TotalLength} is invalid.Maximum length is " +
                   item.Length + " characters\");";
                data += "\r\n";
            }
        }
    }

    if (File.Exists($"../../../Validators/{tableName}.txt"))
    {
        File.Delete($"../../../Validators/{tableName}.txt");
    }

    using StreamWriter file = new($"../../../Validators/{tableName}.txt", append: true, new UTF8Encoding(false));
    await file.WriteLineAsync(data);
}

class FieldData
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public int Length { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPrimary { get; set; }
}