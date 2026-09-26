using System.Drawing.Printing;

namespace ZatcaPos;

/// <summary>Touch-first shell: a left rail of big buttons and one page at a time.</summary>
public sealed class MainForm : Form
{
    internal static readonly Color Ink = Color.FromArgb(28, 37, 33), Green = Color.FromArgb(22, 110, 78), Paper = Color.FromArgb(246, 247, 244), Line = Color.FromArgb(222, 226, 219);
    internal static Font F(float size, FontStyle s = FontStyle.Regular) => new("Segoe UI", size, s);

    private readonly Db _db;
    private readonly Panel _page = new() { Dock = DockStyle.Fill, BackColor = Paper };
    private readonly Dictionary<string, Func<Control>> _pages;
    private readonly FlowLayoutPanel _rail = new() { Dock = DockStyle.Left, Width = 172, FlowDirection = FlowDirection.TopDown, BackColor = Ink, Padding = new Padding(10, 14, 10, 10), WrapContents = false };

    public MainForm(Db db)
    {
        _db = db;
        Text = "Qahwa POS - ZATCA-ready offline point of sale";
        Size = new Size(1280, 800); MinimumSize = new Size(1100, 700);
        Font = F(11f); BackColor = Paper;
        _pages = new()
        {
            ["Sale  البيع"] = () => new SalePage(_db),
            ["Inventory  المخزون"] = () => new InventoryPage(_db),
            ["Reports  التقارير"] = () => new ReportsPage(_db),
            ["Customers  العملاء"] = () => new CustomersPage(_db),
            ["Settings  الإعدادات"] = () => new SettingsPage(_db),
        };
        _rail.Controls.Add(new Label { Text = "Qahwa POS", ForeColor = Color.White, Font = F(15f, FontStyle.Bold), AutoSize = true, Margin = new Padding(4, 0, 0, 18) });
        foreach (var name in _pages.Keys)
        {
            var b = new Button { Text = name.Replace("  ", "\n"), Width = 150, Height = 76, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Font = F(11f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10), Tag = name };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => Show(name);
            _rail.Controls.Add(b);
        }
        Controls.Add(_page); Controls.Add(_rail);
        Show(_pages.Keys.First());
    }

    public void Show(string name)
    {
        foreach (Button b in _rail.Controls.OfType<Button>()) b.BackColor = (string)b.Tag! == name ? Green : Color.FromArgb(44, 56, 50);
        foreach (Control c in _page.Controls) c.Dispose();
        _page.Controls.Clear();
        var p = _pages[name](); p.Dock = DockStyle.Fill; _page.Controls.Add(p);
    }

    internal static Button Big(string text, Color back, int w = 150, int h = 60)
    {
        var b = new Button { Text = text, Width = w, Height = h, BackColor = back, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = F(12f, FontStyle.Bold), Margin = new Padding(4) };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    internal static DataGridView Grid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false, AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, Font = F(11f),
            ColumnHeadersHeight = 40, EnableHeadersVisualStyles = false, GridColor = Line,
        };
        g.RowTemplate.Height = 38;
        g.ColumnHeadersDefaultCellStyle.BackColor = Paper; g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Paper; g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink; g.ColumnHeadersDefaultCellStyle.Font = F(10.5f, FontStyle.Bold);
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 236, 224); g.DefaultCellStyle.SelectionForeColor = Ink;
        return g;
    }
}

// ---------------------------------------------------------------- Sale

public sealed class SalePage : UserControl
{
    private readonly Db _db;
    private readonly List<CartLine> _cart = [];
    private readonly DataGridView _lines = MainForm.Grid();
    private readonly Label _totals = new() { Dock = DockStyle.Bottom, Height = 96, Font = MainForm.F(13f), Padding = new Padding(12, 6, 12, 6), BackColor = Color.White };
    private readonly FlowLayoutPanel _tiles = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
    private readonly Label _customerLabel = new() { AutoSize = true, Font = MainForm.F(11f), Margin = new Padding(8, 18, 8, 0) };
    private Customer? _customer;
    private string _qtyBuffer = "";

    public SalePage(Db db)
    {
        _db = db; BackColor = MainForm.Paper;
        var cats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(10, 8, 10, 0) };
        foreach (var c in new[] { "All" }.Concat(db.Products().Select(p => p.Category).Distinct()))
        {
            var b = MainForm.Big(c, Color.FromArgb(58, 74, 66), 104, 52); b.Font = MainForm.F(10.5f, FontStyle.Bold);
            b.Click += (_, _) => FillTiles(c);
            cats.Controls.Add(b);
        }
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_tiles); left.Controls.Add(cats);

        _lines.Columns.Add("item", "Item"); _lines.Columns.Add("qty", "Qty"); _lines.Columns.Add("amount", "Amount");
        _lines.Columns[1].FillWeight = 30; _lines.Columns[2].FillWeight = 45; _lines.ReadOnly = true;

        var keypad = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 250, ColumnCount = 4, RowCount = 4, Padding = new Padding(6) };
        for (int i = 0; i < 4; i++) { keypad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); keypad.RowStyles.Add(new RowStyle(SizeType.Percent, 25)); }
        string[] keys = ["7", "8", "9", "Qty", "4", "5", "6", "Delete", "1", "2", "3", "Clear", "0", "Client", "", "PAY"];
        foreach (var k in keys)
        {
            var color = k switch { "PAY" => MainForm.Green, "Delete" or "Clear" => Color.FromArgb(160, 64, 52), "Qty" or "Client" => Color.FromArgb(58, 74, 66), _ => Color.FromArgb(92, 104, 98) };
            var b = MainForm.Big(k, color); b.Dock = DockStyle.Fill; b.Visible = k.Length > 0;
            b.Click += (_, _) => Key(k);
            keypad.Controls.Add(b);
        }
        var custBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, BackColor = Color.White };
        custBar.Controls.Add(_customerLabel);

        var right = new Panel { Dock = DockStyle.Right, Width = 450, Padding = new Padding(0, 10, 10, 10) };
        right.Controls.Add(_lines); right.Controls.Add(custBar); right.Controls.Add(_totals); right.Controls.Add(keypad);
        Controls.Add(left); Controls.Add(right);
        FillTiles("All"); Refresh();
    }

    private void FillTiles(string category)
    {
        _tiles.SuspendLayout();
        foreach (Control c in _tiles.Controls) c.Dispose();
        _tiles.Controls.Clear();
        foreach (var p in _db.Products().Where(p => category == "All" || p.Category == category))
        {
            var b = new Button
            {
                Width = 170, Height = 132, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = MainForm.Ink, Margin = new Padding(6),
                Text = $"{p.Name}\n{p.NameAr}\nSAR {Zatca.Money(p.Price)}\n{p.Stock} in stock", Font = MainForm.F(10f), TextAlign = ContentAlignment.MiddleCenter,
                Enabled = p.Stock > 0,
            };
            b.FlatAppearance.BorderColor = MainForm.Line;
            b.Click += (_, _) => Add(p);
            _tiles.Controls.Add(b);
        }
        _tiles.ResumeLayout();
    }

    private void Add(Product p)
    {
        int qty = int.TryParse(_qtyBuffer, out var q) && q > 0 ? q : 1;
        _qtyBuffer = "";
        int i = _cart.FindIndex(l => l.Product.Id == p.Id);
        int newQty = (i >= 0 ? _cart[i].Qty : 0) + qty;
        if (newQty > p.Stock) { MessageBox.Show(this, $"Only {p.Stock} x {p.Name} in stock.", "Stock"); return; }
        if (i >= 0) _cart[i] = _cart[i] with { Qty = newQty }; else _cart.Add(new CartLine(p, qty));
        Refresh();
    }

    private void Key(string k)
    {
        switch (k)
        {
            case "Qty": if (_lines.CurrentRow is { Index: var r } && r < _cart.Count && int.TryParse(_qtyBuffer, out var q) && q > 0) { _cart[r] = _cart[r] with { Qty = Math.Min(q, _cart[r].Product.Stock) }; _qtyBuffer = ""; } break;
            case "Delete": if (_lines.CurrentRow is { Index: var i } && i < _cart.Count) _cart.RemoveAt(i); break;
            case "Clear": _cart.Clear(); _customer = null; _qtyBuffer = ""; break;
            case "Client": PickCustomer(); break;
            case "PAY": Pay(); return;
            default: if (_qtyBuffer.Length < 3) _qtyBuffer += k; break;
        }
        Refresh();
    }

    public new void Refresh()
    {
        _lines.Rows.Clear();
        foreach (var l in _cart) _lines.Rows.Add($"{l.Product.Name}  {l.Product.NameAr}", l.Qty, Zatca.Money(l.Net));
        decimal net = _cart.Sum(l => l.Net), vat = Zatca.Round(net * Zatca.VatRate);
        _totals.Text = $"Subtotal  {Zatca.Money(net)}      VAT 15%  {Zatca.Money(vat)}\nTOTAL  SAR {Zatca.Money(net + vat)}" + (_qtyBuffer.Length > 0 ? $"      qty: {_qtyBuffer}" : "");
        _customerLabel.Text = _customer is null ? "Walk-in customer" : $"Customer: {_customer.Name} · {_customer.Phone} · {_customer.Points} pts";
    }

    private void PickCustomer()
    {
        string phone = Prompt.Show(this, "Customer phone or name", "");
        if (string.IsNullOrWhiteSpace(phone)) return;
        var found = _db.Customers(phone);
        if (found.Count > 0) { _customer = found[0]; return; }
        string name = Prompt.Show(this, "New customer - name", "");
        if (name.Length > 0) { long id = _db.AddCustomer(name, phone); _customer = _db.Customers(phone).First(c => c.Id == id); }
    }

    private void Pay()
    {
        if (_cart.Count == 0) return;
        decimal total = _cart.Sum(l => l.Net); total += Zatca.Round(total * Zatca.VatRate);
        using var dlg = new PaymentDialog(total);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var sale = _db.CompleteSale(_cart, _customer?.Id, dlg.Method, dlg.Tendered);
            var shop = _db.GetSettings();
            using var preview = new PrintPreviewDialog { Document = Invoice.Document(sale, shop, _customer?.Name), Width = 520, Height = 900, UseAntiAlias = true };
            preview.ShowDialog(this);
            _cart.Clear(); _customer = null;
            FillTiles("All"); Refresh();
        }
        catch (InvalidOperationException e) { MessageBox.Show(this, e.Message, "Cannot complete sale"); }
    }
}

public sealed class PaymentDialog : Form
{
    public string Method { get; private set; } = "Cash";
    public decimal Tendered { get; private set; }

    public PaymentDialog(decimal total)
    {
        Text = "Payment"; ClientSize = new Size(460, 300); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; Font = MainForm.F(12f);
        var amount = new TextBox { Text = Zatca.Money(total), Font = MainForm.F(20f), Location = new Point(20, 70), Width = 420 };
        Controls.Add(new Label { Text = $"Total due  SAR {Zatca.Money(total)}", Font = MainForm.F(16f, FontStyle.Bold), AutoSize = true, Location = new Point(20, 18) });
        Controls.Add(amount);
        var cash = MainForm.Big("Cash", MainForm.Green, 200, 70); cash.Location = new Point(20, 140);
        var card = MainForm.Big("Card / mada", Color.FromArgb(40, 84, 140), 200, 70); card.Location = new Point(240, 140);
        cash.Click += (_, _) =>
        {
            if (!decimal.TryParse(amount.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var t) || t < total)
            { MessageBox.Show(this, "Enter the cash received (at least the total)."); return; }
            Method = "Cash"; Tendered = t; DialogResult = DialogResult.OK;
        };
        card.Click += (_, _) => { Method = "Card"; Tendered = total; DialogResult = DialogResult.OK; };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(340, 240), Size = new Size(100, 40) };
        Controls.AddRange([cash, card, cancel]); CancelButton = cancel;
    }
}

// ---------------------------------------------------------------- Inventory

public sealed class InventoryPage : UserControl
{
    private readonly Db _db;
    private readonly DataGridView _grid = MainForm.Grid();

    public InventoryPage(Db db)
    {
        _db = db;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(10) };
        var receive = MainForm.Big("Receive stock (PO)", MainForm.Green, 220);
        var add = MainForm.Big("New product", Color.FromArgb(58, 74, 66), 180);
        var save = MainForm.Big("Save prices", Color.FromArgb(40, 84, 140), 180);
        receive.Click += (_, _) => Receive(); add.Click += (_, _) => AddProduct(); save.Click += (_, _) => Save();
        bar.Controls.AddRange([receive, add, save]);
        foreach (var (n, h) in new[] { ("id", "ID"), ("name", "Product"), ("ar", "الاسم"), ("cat", "Category"), ("price", "Price (excl. VAT)"), ("stock", "Stock"), ("barcode", "Barcode") })
            _grid.Columns.Add(n, h);
        _grid.Columns["id"]!.Visible = false; _grid.Columns["stock"]!.ReadOnly = true;
        var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 10) }; wrap.Controls.Add(_grid);
        Controls.Add(wrap); Controls.Add(bar);
        Load();
    }

    private new void Load()
    {
        _grid.Rows.Clear();
        foreach (var p in _db.Products())
        {
            int r = _grid.Rows.Add(p.Id, p.Name, p.NameAr, p.Category, Zatca.Money(p.Price), p.Stock, p.Barcode);
            if (p.Stock < 20) _grid.Rows[r].Cells["stock"].Style.ForeColor = Color.FromArgb(180, 60, 40);
        }
    }

    private Product? Selected() => _grid.CurrentRow is { } row ? _db.Products().FirstOrDefault(p => p.Id == Convert.ToInt64(row.Cells["id"].Value)) : null;

    private void Receive()
    {
        if (Selected() is not { } p) return;
        if (!int.TryParse(Prompt.Show(this, $"Quantity received for {p.Name}", "24"), out int qty) || qty <= 0) return;
        decimal.TryParse(Prompt.Show(this, "Unit cost (SAR)", Zatca.Money(p.Price * 0.4m)), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var cost);
        string supplier = Prompt.Show(this, "Supplier", "Al-Riyadh Wholesale");
        _db.ReceivePurchase(p.Id, qty, cost, supplier);
        Load();
    }

    private void AddProduct()
    {
        string name = Prompt.Show(this, "Product name (English)", ""); if (name.Length == 0) return;
        string ar = Prompt.Show(this, "اسم المنتج (Arabic)", name);
        string cat = Prompt.Show(this, "Category", "Hot");
        decimal.TryParse(Prompt.Show(this, "Price excl. VAT (SAR)", "10.00"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var price);
        _db.AddProduct(name, ar, cat, price); Load();
    }

    private void Save()
    {
        foreach (DataGridViewRow r in _grid.Rows)
            if (decimal.TryParse(Convert.ToString(r.Cells["price"].Value), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var price))
                _db.UpdateProduct(new Product(Convert.ToInt64(r.Cells["id"].Value), Convert.ToString(r.Cells["name"].Value)!, Convert.ToString(r.Cells["ar"].Value)!,
                    Convert.ToString(r.Cells["cat"].Value)!, price, 0, Convert.ToString(r.Cells["barcode"].Value) ?? ""));
        Load();
        MessageBox.Show(this, "Saved.");
    }
}

// ---------------------------------------------------------------- Reports

public sealed class ReportsPage : UserControl
{
    private readonly Db _db;
    private readonly DataGridView _grid = MainForm.Grid();
    private readonly DateTimePicker _from = new() { Width = 168, Font = MainForm.F(12f), Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = DateTime.Today.AddDays(-7) };
    private readonly DateTimePicker _to = new() { Width = 168, Font = MainForm.F(12f), Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = DateTime.Today };
    private readonly TextBox _product = new() { Width = 170, Font = MainForm.F(12f), PlaceholderText = "Product filter" };
    private readonly TextBox _cashier = new() { Width = 150, Font = MainForm.F(12f), PlaceholderText = "Cashier" };
    private readonly Label _summary = new() { Dock = DockStyle.Bottom, Height = 60, Font = MainForm.F(13f, FontStyle.Bold), Padding = new Padding(14, 16, 0, 0), BackColor = Color.White };
    private List<Sale> _sales = [];

    public ReportsPage(Db db)
    {
        _db = db;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 150, Padding = new Padding(10) };
        var run = MainForm.Big("Show", MainForm.Green, 110); var csv = MainForm.Big("Export Excel", Color.FromArgb(40, 84, 140), 170); var pdf = MainForm.Big("Export PDF", Color.FromArgb(58, 74, 66), 160);
        run.Click += (_, _) => Run(); csv.Click += (_, _) => ExportCsv(); pdf.Click += (_, _) => ExportPdf();
        foreach (Control c in new Control[] { new Label { Text = "From", AutoSize = true, Margin = new Padding(4, 18, 0, 0) }, _from, new Label { Text = "To", AutoSize = true, Margin = new Padding(8, 18, 0, 0) }, _to, _product, _cashier, run, csv, pdf })
        { if (c is TextBox or DateTimePicker) c.Margin = new Padding(6, 14, 6, 0); bar.Controls.Add(c); }
        foreach (var (n, h) in new[] { ("no", "Invoice"), ("date", "Date"), ("cashier", "Cashier"), ("items", "Items"), ("pay", "Payment"), ("net", "Net"), ("vat", "VAT"), ("total", "Total") })
            _grid.Columns.Add(n, h);
        foreach (var (col, w) in new[] { ("no", 110f), ("date", 125f), ("cashier", 90f), ("items", 230f), ("pay", 75f), ("net", 70f), ("vat", 60f), ("total", 75f) }) _grid.Columns[col]!.FillWeight = w;
        var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 10) }; wrap.Controls.Add(_grid);
        Controls.Add(wrap); Controls.Add(_summary); Controls.Add(bar);
        Run();
    }

    private void Run()
    {
        _sales = _db.Sales(_from.Value.Date, _to.Value.Date.AddDays(1), _product.Text, null, _cashier.Text);
        _grid.Rows.Clear();
        foreach (var s in _sales)
            _grid.Rows.Add(s.Number, s.IssuedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), s.Cashier, string.Join(", ", s.Lines.Select(l => $"{l.Qty}× {l.Name}")),
                s.Payment, Zatca.Money(s.Net), Zatca.Money(s.Vat), Zatca.Money(s.Total));
        string chain = _db.VerifyChain() is { } broken ? $"   ⚠ hash chain broken at {broken}" : "   ✓ invoice hash chain intact";
        _summary.Text = $"{_sales.Count} invoices    Net {Zatca.Money(_sales.Sum(s => s.Net))}    VAT {Zatca.Money(_sales.Sum(s => s.Vat))}    Total SAR {Zatca.Money(_sales.Sum(s => s.Total))}{chain}";
    }

    private void ExportCsv()
    {
        using var dlg = new SaveFileDialog { Filter = "Excel CSV (*.csv)|*.csv", FileName = $"sales-{_from.Value:yyyyMMdd}-{_to.Value:yyyyMMdd}.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Reports.WriteCsv(dlg.FileName, _sales);
    }

    private void ExportPdf()
    {
        var doc = Reports.PrintDocument(_sales, _db.GetSettings(), _from.Value, _to.Value);
        doc.PrinterSettings.PrinterName = "Microsoft Print to PDF"; // Windows' built-in PDF printer
        using var dlg = new PrintDialog { Document = doc, UseEXDialog = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) doc.Print();
    }
}

public static class Reports
{
    public static void WriteCsv(string path, IEnumerable<Sale> sales)
    {
        var sb = new System.Text.StringBuilder("Invoice,Date,Cashier,Items,Payment,Net,VAT,Total\r\n");
        foreach (var s in sales)
            sb.Append($"{s.Number},{s.IssuedUtc.ToLocalTime():yyyy-MM-dd HH:mm},{s.Cashier},\"{string.Join("; ", s.Lines.Select(l => $"{l.Qty}x {l.Name}"))}\",{s.Payment},{Zatca.Money(s.Net)},{Zatca.Money(s.Vat)},{Zatca.Money(s.Total)}\r\n");
        File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true)); // BOM so Excel reads Arabic correctly
    }

    public static PrintDocument PrintDocument(List<Sale> sales, Settings shop, DateTime from, DateTime to)
    {
        var doc = new PrintDocument { DocumentName = "Sales report" };
        int index = 0;
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!; float y = e.MarginBounds.Top; float x = e.MarginBounds.Left;
            using var h = MainForm.F(14f, FontStyle.Bold); using var f = MainForm.F(9f);
            if (index == 0)
            {
                g.DrawString($"{shop.SellerName} - sales report {from:yyyy-MM-dd} to {to:yyyy-MM-dd}", h, Brushes.Black, x, y); y += 34;
                g.DrawString($"{sales.Count} invoices   Net {Zatca.Money(sales.Sum(s => s.Net))}   VAT {Zatca.Money(sales.Sum(s => s.Vat))}   Total SAR {Zatca.Money(sales.Sum(s => s.Total))}", f, Brushes.Black, x, y); y += 26;
            }
            for (; index < sales.Count && y < e.MarginBounds.Bottom - 20; index++, y += 18)
            {
                var s = sales[index];
                g.DrawString($"{s.Number}  {s.IssuedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {s.Cashier,-10}  {s.Payment,-5}  net {Zatca.Money(s.Net),8}  vat {Zatca.Money(s.Vat),7}  total {Zatca.Money(s.Total),8}", f, Brushes.Black, x, y);
            }
            e.HasMorePages = index < sales.Count;
        };
        return doc;
    }
}

// ---------------------------------------------------------------- Customers & settings

public sealed class CustomersPage : UserControl
{
    public CustomersPage(Db db)
    {
        var grid = MainForm.Grid();
        var search = new TextBox { Width = 320, Font = MainForm.F(14f), PlaceholderText = "Search name or phone", Margin = new Padding(10, 16, 10, 0) };
        foreach (var (n, h) in new[] { ("name", "Name"), ("phone", "Phone"), ("points", "Loyalty points") }) grid.Columns.Add(n, h);
        void Fill() { grid.Rows.Clear(); foreach (var c in db.Customers(search.Text)) grid.Rows.Add(c.Name, c.Phone, c.Points); }
        search.TextChanged += (_, _) => Fill();
        var add = MainForm.Big("Add customer", MainForm.Green, 190);
        add.Click += (_, _) =>
        {
            string name = Prompt.Show(this, "Customer name", ""); if (name.Length == 0) return;
            string phone = Prompt.Show(this, "Phone", "05");
            try { db.AddCustomer(name, phone); Fill(); } catch (Exception e) { MessageBox.Show(this, e.Message); }
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(10) };
        bar.Controls.AddRange([search, add]);
        var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 10) }; wrap.Controls.Add(grid);
        Controls.Add(wrap); Controls.Add(bar);
        Fill();
    }
}

public sealed class SettingsPage : UserControl
{
    public SettingsPage(Db db)
    {
        var s = db.GetSettings();
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(20) };
        TextBox Field(string label, string value)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 14, 16, 0) });
            var box = new TextBox { Text = value, Width = 420, Font = MainForm.F(13f), Margin = new Padding(0, 8, 0, 0) }; t.Controls.Add(box); return box;
        }
        var seller = Field("Seller name (English)", s.SellerName);
        var sellerAr = Field("اسم المنشأة (Arabic, printed in QR)", s.SellerNameAr);
        var vat = Field("VAT registration number", s.VatNumber);
        var addr = Field("Address", s.Address);
        var cashier = Field("Cashier on this terminal", s.Cashier);
        var save = MainForm.Big("Save", MainForm.Green, 140);
        save.Click += (_, _) =>
        {
            if (!Zatca.IsValidVatNumber(vat.Text.Trim())) { MessageBox.Show(this, "A Saudi VAT number has 15 digits and starts and ends with 3."); return; }
            db.SaveSettings(new Settings(seller.Text.Trim(), sellerAr.Text.Trim(), vat.Text.Trim(), addr.Text.Trim(), cashier.Text.Trim()));
            MessageBox.Show(this, "Saved.");
        };
        t.Controls.Add(new Label()); t.Controls.Add(save);
        Controls.Add(t);
    }
}

internal static class Prompt
{
    public static string Show(IWin32Window owner, string question, string initial)
    {
        using var f = new Form { Text = "Qahwa POS", ClientSize = new Size(460, 170), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, Font = MainForm.F(12f) };
        var box = new TextBox { Text = initial, Location = new Point(20, 56), Width = 420, Font = MainForm.F(14f) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(240, 110), Size = new Size(95, 44) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(345, 110), Size = new Size(95, 44) };
        f.Controls.AddRange([new Label { Text = question, AutoSize = true, Location = new Point(20, 18) }, box, ok, cancel]);
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : "";
    }
}
