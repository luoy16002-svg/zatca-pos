using Microsoft.Data.Sqlite;

namespace ZatcaPos;

public sealed record Product(long Id, string Name, string NameAr, string Category, decimal Price, int Stock, string Barcode);
public sealed record Customer(long Id, string Name, string Phone, int Points);
public sealed record CartLine(Product Product, int Qty)
{
    public decimal Net => Zatca.Round(Product.Price * Qty);
}
public sealed record Sale(long Id, string Number, string Uuid, DateTime IssuedUtc, string Cashier, long? CustomerId, decimal Net, decimal Vat, decimal Total,
    string Payment, decimal Tendered, string Qr, string Hash, string PreviousHash, List<(string Name, string NameAr, int Qty, decimal Price, decimal Net)> Lines);
public sealed record Settings(string SellerName, string SellerNameAr, string VatNumber, string Address, string Cashier);

/// <summary>Everything lives in one SQLite file, so the POS works with no network at all.</summary>
public sealed class Db : IDisposable
{
    private readonly SqliteConnection _c;

    public Db(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _c = new SqliteConnection($"Data Source={path}");
        _c.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        Exec("""
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS products(id INTEGER PRIMARY KEY, name TEXT NOT NULL, name_ar TEXT NOT NULL, category TEXT NOT NULL,
                price TEXT NOT NULL, stock INTEGER NOT NULL, barcode TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS customers(id INTEGER PRIMARY KEY, name TEXT NOT NULL, phone TEXT NOT NULL UNIQUE, points INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS sales(id INTEGER PRIMARY KEY, number TEXT NOT NULL UNIQUE, uuid TEXT NOT NULL, issued_utc TEXT NOT NULL,
                cashier TEXT NOT NULL, customer_id INTEGER REFERENCES customers(id), net TEXT NOT NULL, vat TEXT NOT NULL, total TEXT NOT NULL,
                payment TEXT NOT NULL, tendered TEXT NOT NULL, qr TEXT NOT NULL, hash TEXT NOT NULL, previous_hash TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sale_items(sale_id INTEGER NOT NULL REFERENCES sales(id), product_id INTEGER NOT NULL REFERENCES products(id),
                name TEXT NOT NULL, name_ar TEXT NOT NULL, qty INTEGER NOT NULL, price TEXT NOT NULL, net TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS purchase_orders(id INTEGER PRIMARY KEY, product_id INTEGER NOT NULL REFERENCES products(id),
                qty INTEGER NOT NULL, cost TEXT NOT NULL, supplier TEXT NOT NULL, received_utc TEXT NOT NULL);
            """);
        if (Scalar("SELECT COUNT(*) FROM products") == 0) Seed();
    }

    public void Dispose() => _c.Dispose();

    private void Exec(string sql, params (string, object?)[] p) { using var cmd = Cmd(sql, p); cmd.ExecuteNonQuery(); }
    private long Scalar(string sql, params (string, object?)[] p) { using var cmd = Cmd(sql, p); return Convert.ToInt64(cmd.ExecuteScalar() ?? 0); }
    private SqliteCommand Cmd(string sql, (string, object?)[] p, SqliteTransaction? tx = null)
    {
        var cmd = _c.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = tx;
        foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd;
    }
    private static decimal Dec(object o) => decimal.Parse((string)o, System.Globalization.CultureInfo.InvariantCulture);
    private static string S(decimal d) => Zatca.Money(d);

    private void Seed()
    {
        var settings = new Settings("Qahwa Corner Cafe", "مقهى زاوية القهوة", "310122393500003", "King Fahd Rd, Riyadh", "Cashier 1");
        SaveSettings(settings);
        (string, string, string, decimal, int)[] items =
        [
            ("Arabic Coffee", "قهوة عربية", "Hot", 12m, 120), ("Cappuccino", "كابتشينو", "Hot", 16m, 90),
            ("Flat White", "فلات وايت", "Hot", 17m, 80), ("Karak Tea", "شاي كرك", "Hot", 8m, 150),
            ("Iced Latte", "آيس لاتيه", "Cold", 18m, 70), ("Fresh Orange", "برتقال طازج", "Cold", 14m, 40),
            ("Mineral Water", "مياه معدنية", "Cold", 3m, 200), ("Date Cake", "كيكة التمر", "Bakery", 15m, 25),
            ("Croissant", "كرواسون", "Bakery", 9m, 30), ("Kunafa", "كنافة", "Bakery", 22m, 18),
            ("Halloumi Sandwich", "ساندويتش حلومي", "Food", 24m, 20), ("Chicken Wrap", "راب دجاج", "Food", 26m, 22),
        ];
        int n = 0;
        foreach (var (name, ar, cat, price, stock) in items)
            Exec("INSERT INTO products(name,name_ar,category,price,stock,barcode) VALUES($n,$a,$c,$p,$s,$b)",
                ("$n", name), ("$a", ar), ("$c", cat), ("$p", S(price)), ("$s", stock), ("$b", $"62810000{++n:D5}"));
        Exec("INSERT INTO customers(name,phone,points) VALUES('Abdullah Al-Harbi','0551234567',40),('Noura Al-Qahtani','0569876543',115)");
    }

    // ---------- settings ----------

    public Settings GetSettings()
    {
        var d = new Dictionary<string, string>();
        using var cmd = Cmd("SELECT key, value FROM settings", []);
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = r.GetString(1);
        return new Settings(d.GetValueOrDefault("seller", ""), d.GetValueOrDefault("seller_ar", ""), d.GetValueOrDefault("vat", ""),
            d.GetValueOrDefault("address", ""), d.GetValueOrDefault("cashier", "Cashier 1"));
    }

    public void SaveSettings(Settings s)
    {
        foreach (var (k, v) in new[] { ("seller", s.SellerName), ("seller_ar", s.SellerNameAr), ("vat", s.VatNumber), ("address", s.Address), ("cashier", s.Cashier) })
            Exec("INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$k", k), ("$v", v));
    }

    // ---------- products & stock ----------

    public List<Product> Products()
    {
        var list = new List<Product>();
        using var cmd = Cmd("SELECT id,name,name_ar,category,price,stock,barcode FROM products ORDER BY category, name", []);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Product(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Dec(r.GetValue(4)), r.GetInt32(5), r.GetString(6)));
        return list;
    }

    public void UpdateProduct(Product p) => Exec("UPDATE products SET name=$n,name_ar=$a,category=$c,price=$p,barcode=$b WHERE id=$id",
        ("$n", p.Name), ("$a", p.NameAr), ("$c", p.Category), ("$p", S(p.Price)), ("$b", p.Barcode), ("$id", p.Id));

    public void AddProduct(string name, string nameAr, string category, decimal price) =>
        Exec("INSERT INTO products(name,name_ar,category,price,stock) VALUES($n,$a,$c,$p,0)", ("$n", name), ("$a", nameAr), ("$c", category), ("$p", S(price)));

    /// <summary>Simple purchase-order entry: receiving goods adds stock and records the cost.</summary>
    public void ReceivePurchase(long productId, int qty, decimal unitCost, string supplier)
    {
        using var tx = _c.BeginTransaction();
        using (var a = Cmd("INSERT INTO purchase_orders(product_id,qty,cost,supplier,received_utc) VALUES($p,$q,$c,$s,$t)",
            [("$p", productId), ("$q", qty), ("$c", S(unitCost)), ("$s", supplier), ("$t", DateTime.UtcNow.ToString("O"))], tx)) a.ExecuteNonQuery();
        using (var b = Cmd("UPDATE products SET stock = stock + $q WHERE id=$p", [("$q", qty), ("$p", productId)], tx)) b.ExecuteNonQuery();
        tx.Commit();
    }

    // ---------- customers ----------

    public List<Customer> Customers(string? search = null)
    {
        var list = new List<Customer>();
        using var cmd = Cmd("SELECT id,name,phone,points FROM customers WHERE $q IS NULL OR name LIKE '%'||$q||'%' OR phone LIKE '%'||$q||'%' ORDER BY name",
            [("$q", string.IsNullOrWhiteSpace(search) ? null : search.Trim())]);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Customer(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
        return list;
    }

    public long AddCustomer(string name, string phone)
    {
        Exec("INSERT INTO customers(name,phone) VALUES($n,$p)", ("$n", name), ("$p", phone));
        return Scalar("SELECT last_insert_rowid()");
    }

    // ---------- sales ----------

    /// <summary>Records the sale, deducts stock, adds loyalty points (1 per 10 SAR) and chains the invoice hash, all in one transaction.</summary>
    public Sale CompleteSale(IReadOnlyList<CartLine> cart, long? customerId, string payment, decimal tendered)
    {
        if (cart.Count == 0) throw new InvalidOperationException("The cart is empty.");
        var s = GetSettings();
        decimal net = cart.Sum(l => l.Net), vat = Zatca.Round(net * Zatca.VatRate), total = net + vat;
        if (payment == "Cash" && tendered < total) throw new InvalidOperationException("Cash tendered is less than the total.");
        foreach (var l in cart)
        {
            long stock = Scalar("SELECT stock FROM products WHERE id=$id", ("$id", l.Product.Id));
            if (stock < l.Qty) throw new InvalidOperationException($"Only {stock} x {l.Product.Name} left in stock.");
        }

        using var tx = _c.BeginTransaction();
        long counter;
        string previous;
        using (var q = Cmd("SELECT COUNT(*), (SELECT hash FROM sales ORDER BY id DESC LIMIT 1) FROM sales", [], tx))
        using (var r = q.ExecuteReader()) { r.Read(); counter = r.GetInt64(0) + 1; previous = r.IsDBNull(1) ? Zatca.FirstPreviousHash : r.GetString(1); }

        var issued = DateTime.UtcNow;
        string number = $"INV-{counter:D6}", uuid = Guid.NewGuid().ToString();
        string qr = Zatca.BuildQr(s.SellerNameAr.Length > 0 ? s.SellerNameAr : s.SellerName, s.VatNumber, issued, total, vat);
        string hash = Zatca.InvoiceHash(previous, number, uuid, issued, total, vat);

        long saleId;
        using (var ins = Cmd("""
            INSERT INTO sales(number,uuid,issued_utc,cashier,customer_id,net,vat,total,payment,tendered,qr,hash,previous_hash)
            VALUES($no,$u,$t,$ca,$cu,$n,$v,$to,$p,$te,$qr,$h,$ph); SELECT last_insert_rowid();
            """, [("$no", number), ("$u", uuid), ("$t", issued.ToString("O")), ("$ca", s.Cashier), ("$cu", customerId), ("$n", S(net)), ("$v", S(vat)),
                  ("$to", S(total)), ("$p", payment), ("$te", S(payment == "Cash" ? tendered : total)), ("$qr", qr), ("$h", hash), ("$ph", previous)], tx))
            saleId = Convert.ToInt64(ins.ExecuteScalar());
        foreach (var l in cart)
        {
            using (var it = Cmd("INSERT INTO sale_items VALUES($s,$p,$n,$a,$q,$pr,$ne)",
                [("$s", saleId), ("$p", l.Product.Id), ("$n", l.Product.Name), ("$a", l.Product.NameAr), ("$q", l.Qty), ("$pr", S(l.Product.Price)), ("$ne", S(l.Net))], tx))
                it.ExecuteNonQuery();
            using (var st = Cmd("UPDATE products SET stock = stock - $q WHERE id=$id", [("$q", l.Qty), ("$id", l.Product.Id)], tx)) st.ExecuteNonQuery();
        }
        if (customerId is long cid)
            using (var pts = Cmd("UPDATE customers SET points = points + $p WHERE id=$id", [("$p", (int)(total / 10)), ("$id", cid)], tx)) pts.ExecuteNonQuery();
        tx.Commit();
        return GetSale(saleId)!;
    }

    public Sale? GetSale(long id) => Sales(null, null, null, id).FirstOrDefault();

    public List<Sale> Sales(DateTime? fromLocal, DateTime? toLocal, string? productFilter, long? onlyId = null, string? cashier = null)
    {
        var list = new List<Sale>();
        using var cmd = Cmd("""
            SELECT id,number,uuid,issued_utc,cashier,customer_id,net,vat,total,payment,tendered,qr,hash,previous_hash FROM sales
            WHERE ($id IS NULL OR id=$id) AND ($from IS NULL OR issued_utc >= $from) AND ($to IS NULL OR issued_utc < $to)
              AND ($c IS NULL OR cashier=$c)
              AND ($pf IS NULL OR id IN (SELECT sale_id FROM sale_items WHERE name LIKE '%'||$pf||'%' OR name_ar LIKE '%'||$pf||'%'))
            ORDER BY id DESC
            """, [("$id", onlyId), ("$from", fromLocal?.ToUniversalTime().ToString("O")), ("$to", toLocal?.ToUniversalTime().ToString("O")),
                  ("$c", string.IsNullOrWhiteSpace(cashier) ? null : cashier), ("$pf", string.IsNullOrWhiteSpace(productFilter) ? null : productFilter)]);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Sale(r.GetInt64(0), r.GetString(1), r.GetString(2), DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetInt64(5), Dec(r.GetValue(6)), Dec(r.GetValue(7)), Dec(r.GetValue(8)), r.GetString(9), Dec(r.GetValue(10)),
                r.GetString(11), r.GetString(12), r.GetString(13), []));
        foreach (var s in list)
        {
            using var li = Cmd("SELECT name,name_ar,qty,price,net FROM sale_items WHERE sale_id=$s", [("$s", s.Id)]);
            using var lr = li.ExecuteReader();
            while (lr.Read()) s.Lines.Add((lr.GetString(0), lr.GetString(1), lr.GetInt32(2), Dec(lr.GetValue(3)), Dec(lr.GetValue(4))));
        }
        return list;
    }

    /// <summary>Recomputes the whole hash chain; returns the first broken invoice number, or null when intact.</summary>
    public string? VerifyChain()
    {
        string prev = Zatca.FirstPreviousHash;
        foreach (var s in Sales(null, null, null).OrderBy(s => s.Id))
        {
            if (s.PreviousHash != prev || Zatca.InvoiceHash(prev, s.Number, s.Uuid, s.IssuedUtc, s.Total, s.Vat) != s.Hash) return s.Number;
            prev = s.Hash;
        }
        return null;
    }
}
