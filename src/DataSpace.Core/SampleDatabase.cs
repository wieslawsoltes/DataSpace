namespace DataSpace.Core;

public static class SampleDatabase
{
    private static FieldDefinition F(string name, FieldType type = FieldType.ShortText, double width = 150, bool required = false) => new()
    { Name = name, Type = type, Width = width, Required = required, PrimaryKey = type == FieldType.AutoNumber };
    private static void Row(TableDefinition table, params string?[] values) => RecordOperations.Insert(table,
        table.Fields.Select((f, i) => KeyValuePair.Create(f.Name, values[i])).ToDictionary(p => p.Key, p => p.Value));

    public static DatabaseDocument Create()
    {
        var database = new DatabaseDocument { Name = "Northwind" };
        var customers = new TableDefinition { Name = "Customers", Description = "Customer contact and account information", Fields =
            [F("ID", FieldType.AutoNumber, 65), F("Company", width: 230, required: true), F("Contact Name", width: 165), F("Job Title", width: 155), F("City", width: 125), F("Country", width: 115), F("Email", width: 235), F("Business Phone", width: 165), F("Active", FieldType.YesNo, 80)], DiagramX = 45, DiagramY = 45 };
        string[][] contacts =
        [
            ["Northwind Traders", "Anna Kowalska", "Purchasing Manager", "Warsaw", "Poland", "anna@northwind.example", "+48 22 555 0101"],
            ["Alpine Ski House", "Thomas Berg", "Owner", "Innsbruck", "Austria", "thomas@alpine.example", "+43 512 555 012"],
            ["Blue Yonder Airlines", "James Wilson", "Operations Manager", "London", "UK", "james@blueyonder.example", "+44 20 555 0103"],
            ["Contoso Retail", "Elena Rossi", "Sales Director", "Milan", "Italy", "elena@contoso.example", "+39 02 555 0104"],
            ["Fabrikam Industries", "Daniel Weber", "Account Manager", "Berlin", "Germany", "daniel@fabrikam.example", "+49 30 555 0105"],
            ["Fourth Coffee", "Sophie Martin", "Buyer", "Paris", "France", "sophie@fourth.example", "+33 1 555 0106"],
            ["Graphic Design Institute", "Olivia Chen", "Office Manager", "Seattle", "USA", "olivia@graphic.example", "+1 206 555 0107"],
            ["Humongous Insurance", "William Brown", "Director", "Manchester", "UK", "william@humongous.example", "+44 161 555 0108"],
            ["Litware", "Ava Johnson", "Purchasing Agent", "Boston", "USA", "ava@litware.example", "+1 617 555 0109"],
            ["Lucerne Publishing", "Lukas Meier", "Marketing Manager", "Lucerne", "Switzerland", "lukas@lucerne.example", "+41 41 555 0110"],
            ["Proseware", "Emma Davis", "Owner", "Dublin", "Ireland", "emma@proseware.example", "+353 1 555 0111"],
            ["Tailspin Toys", "Noah Taylor", "Sales Manager", "Bristol", "UK", "noah@tailspin.example", "+44 117 555 0112"],
            ["The Phone Company", "Mia Anderson", "Buyer", "Stockholm", "Sweden", "mia@phone.example", "+46 8 555 0113"],
            ["Trey Research", "Oliver White", "Research Director", "Cambridge", "UK", "oliver@trey.example", "+44 1223 555 0114"],
            ["Wide World Importers", "Charlotte Garcia", "Import Manager", "Barcelona", "Spain", "charlotte@wideworld.example", "+34 93 555 0115"],
            ["Woodgrove Bank", "Henry Müller", "Procurement Lead", "Zurich", "Switzerland", "henry@woodgrove.example", "+41 44 555 0116"],
            ["Adventure Works", "Amelia Moore", "Owner", "Portland", "USA", "amelia@adventure.example", "+1 503 555 0117"],
            ["Consolidated Messenger", "Leon Fischer", "Logistics Manager", "Munich", "Germany", "leon@consolidated.example", "+49 89 555 0118"]
        ];
        foreach (var contact in contacts) Row(customers, [null, .. contact, "True"]);
        database.Tables.Add(customers);
        var products = new TableDefinition { Name = "Products", DiagramX = 650, DiagramY = 50, Fields =
            [F("ID", FieldType.AutoNumber, 65), F("Product Name", width: 230, required: true), F("Category", width: 150), F("Unit Price", FieldType.Currency, 125), F("Units In Stock", FieldType.Integer, 130), F("Discontinued", FieldType.YesNo, 115)] };
        string[] names = ["Chai", "Chang", "Aniseed Syrup", "Chef Anton's Cajun Seasoning", "Grandma's Boysenberry Spread", "Uncle Bob's Organic Pears", "Northwoods Cranberry Sauce", "Mishi Kobe Niku", "Ikura", "Queso Cabrales", "Tofu", "Alice Mutton"];
        for (var i = 0; i < names.Length; i++) Row(products, null, names[i], new[] { "Beverages", "Condiments", "Produce", "Meat & Poultry" }[i % 4], (18m + i * 3.25m).ToString(FieldValues.Culture), (39 + i * 7).ToString(), "False");
        database.Tables.Add(products);
        var orders = new TableDefinition { Name = "Orders", DiagramX = 345, DiagramY = 45, Fields =
            [F("ID", FieldType.AutoNumber, 75), F("Customer ID", FieldType.Integer, 120, true), F("Order Date", FieldType.DateTime, 140), F("Status", width: 140), F("Amount", FieldType.Currency, 135), F("Paid", FieldType.YesNo, 85)] };
        for (var i = 0; i < 48; i++) Row(orders, null, (i % contacts.Length + 1).ToString(), new DateTime(2026, 9, 1).AddDays(i % 27).ToString("O"), new[] { "Shipped", "Processing", "New", "Shipped" }[i % 4], (149.5m + i * 37.25m).ToString(FieldValues.Culture), i % 3 == 0 ? "False" : "True");
        database.Tables.Add(orders);
        var details = new TableDefinition { Name = "Order Details", DiagramX = 345, DiagramY = 340, Fields =
            [F("ID", FieldType.AutoNumber, 70), F("Order ID", FieldType.Integer, 110, true), F("Product ID", FieldType.Integer, 110, true), F("Quantity", FieldType.Integer, 110), F("Unit Price", FieldType.Currency, 125)] };
        for (var i = 0; i < 96; i++) Row(details, null, (i / 2 + 1).ToString(), (i % names.Length + 1).ToString(), (i % 8 + 1).ToString(), (18m + i % names.Length * 3.25m).ToString(FieldValues.Culture));
        database.Tables.Add(details);
        database.Relationships.AddRange([
            new() { Name = "Customers_Orders", ParentTable = "Customers", ParentField = "ID", ChildTable = "Orders", ChildField = "Customer ID" },
            new() { Name = "Orders_Details", ParentTable = "Orders", ParentField = "ID", ChildTable = "Order Details", ChildField = "Order ID", CascadeDelete = true },
            new() { Name = "Products_Details", ParentTable = "Products", ParentField = "ID", ChildTable = "Order Details", ChildField = "Product ID" }
        ]);
        database.Queries.AddRange([
            new() { Name = "Customers in the UK", Sql = "SELECT [Company], [Contact Name], [City], [Email] FROM [Customers] WHERE [Country] = 'UK' ORDER BY [Company];" },
            new() { Name = "Open Orders", Sql = "SELECT o.[ID], c.[Company], o.[Order Date], o.[Status], o.[Amount] FROM [Orders] AS o INNER JOIN [Customers] AS c ON o.[Customer ID] = c.[ID] WHERE o.[Paid] = False ORDER BY o.[Order Date] DESC;" },
            new() { Name = "Sales by Customer", Sql = "SELECT c.[Company], Count(o.[ID]) AS [Orders], Sum(o.[Amount]) AS [Total Sales] FROM [Customers] AS c INNER JOIN [Orders] AS o ON c.[ID] = o.[Customer ID] GROUP BY c.[Company] ORDER BY [Total Sales] DESC;" }
        ]);
        database.Forms.Add(CreateForm(customers, "Customer Details"));
        database.Forms.Add(CreateForm(products, "Product Details"));
        database.Reports.Add(new() { Name = "Customer Directory", Title = "Customer directory", Source = "Customers", Fields = ["Company", "Contact Name", "City", "Country"], Landscape = true });
        database.Reports.Add(new() { Name = "Sales Summary", Title = "Sales by customer", Source = "Sales by Customer", Fields = ["Company", "Orders", "Total Sales"] });
        database.Macros.Add(new() { Name = "Open Customer Directory", Steps = [new() { Action = MacroActionKind.OpenObject, Argument = "Customer Directory" }] });
        SchemaValidator.Validate(database);
        return database;
    }

    public static FormDefinition CreateForm(TableDefinition table, string name)
    {
        var form = new FormDefinition { Name = name, Source = table.Name, Title = table.Name };
        for (var i = 0; i < table.Fields.Count; i++)
        {
            var field = table.Fields[i];
            form.Controls.Add(new() { Kind = field.Type == FieldType.YesNo ? LayoutControlKind.CheckBox : LayoutControlKind.TextBox,
                Field = field.Name, Caption = field.DisplayName, X = 38 + (i % 2) * 390, Y = 100 + (i / 2) * 88, Width = 340, Height = 32 });
        }
        form.Height = Math.Max(600, 150 + (table.Fields.Count + 1) / 2 * 88);
        return form;
    }
}
