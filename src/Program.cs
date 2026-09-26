using System.Runtime.InteropServices;

namespace ZatcaPos;

internal static class Program
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

    private static string DbPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QahwaPOS", "pos.db");

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["selftest"]) { AttachConsole(-1); return SelfTest(); }
        if (args is ["render", var dir]) return Render(dir);
        ApplicationConfiguration.Initialize();
        using var db = new Db(DbPath);
        Application.Run(new MainForm(db));
        return 0;
    }

    private static int SelfTest()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pos-selftest-{Guid.NewGuid():N}.db");
        bool ok = true;
        void Check(string what, bool pass) { ok &= pass; Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {what}"); }
        using (var db = new Db(path))
        {
            var p = db.Products();
            var coffee = p.First(x => x.Name == "Cappuccino"); var cake = p.First(x => x.Name == "Date Cake");
            var cust = db.Customers("0551234567").Single();
            var sale = db.CompleteSale([new CartLine(coffee, 2), new CartLine(cake, 1)], cust.Id, "Cash", 100m);
            Check("net = 2 x 16 + 15 = 47.00", sale.Net == 47m);
            Check("VAT 15% = 7.05, total = 54.05", sale.Vat == 7.05m && sale.Total == 54.05m);
            Check("stock deducted (90 -> 88, 25 -> 24)", db.Products().First(x => x.Id == coffee.Id).Stock == 88 && db.Products().First(x => x.Id == cake.Id).Stock == 24);
            Check("loyalty points added (40 + 5)", db.Customers("0551234567").Single().Points == 45);
            var qr = Zatca.ParseQr(sale.Qr);
            Check($"QR TLV: seller '{qr[1]}', VAT {qr[2]}, time {qr[3]}, total {qr[4]}, vat {qr[5]}",
                qr[1] == "مقهى زاوية القهوة" && qr[2] == "310122393500003" && qr[4] == "54.05" && qr[5] == "7.05" && qr[3].EndsWith('Z'));
            Check("first invoice chains from ZATCA's initial PIH", sale.PreviousHash == "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI0NjcyOWQ3M2EyN2ZiNTdlOQ==");
            var second = db.CompleteSale([new CartLine(coffee, 1)], null, "Card", 0);
            Check("second invoice number INV-000002 chains to the first", second.Number == "INV-000002" && second.PreviousHash == sale.Hash);
            Check("hash chain verifies", db.VerifyChain() is null);
            try { db.CompleteSale([new CartLine(cake, 999)], null, "Card", 0); Check("over-selling blocked", false); }
            catch (InvalidOperationException) { Check("over-selling blocked", true); }
            db.ReceivePurchase(cake.Id, 12, 6m, "Test supplier");
            Check("purchase order adds stock (24 -> 36)", db.Products().First(x => x.Id == cake.Id).Stock == 36);
            Check("report filter by product finds 1 sale with Date Cake", db.Sales(DateTime.Today, DateTime.Today.AddDays(1), "Date Cake").Count == 1);
            string csv = Path.ChangeExtension(path, ".csv"); Reports.WriteCsv(csv, db.Sales(null, null, null));
            Check("CSV export has header + 2 rows", File.ReadAllLines(csv).Length == 3); File.Delete(csv);
        }
        using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            raw.Open(); using var cmd = raw.CreateCommand(); cmd.CommandText = "UPDATE sales SET total='1.00' WHERE number='INV-000001'"; cmd.ExecuteNonQuery();
        }
        using (var db = new Db(path)) Check("tampered invoice detected by the hash chain", db.VerifyChain() == "INV-000001");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        Console.WriteLine(ok ? "ALL CHECKS PASSED" : "SOME CHECKS FAILED");
        return ok ? 0 : 1;
    }

    /// <summary>Writes screenshots of every page plus a sample receipt, drawn off-screen, for the README.</summary>
    private static int Render(string dir)
    {
        Directory.CreateDirectory(dir);
        ApplicationConfiguration.Initialize();
        string path = Path.Combine(Path.GetTempPath(), $"pos-render-{Guid.NewGuid():N}.db");
        using (var db = new Db(path))
        {
            var p = db.Products();
            var sale = db.CompleteSale([new CartLine(p.First(x => x.Name == "Flat White"), 2), new CartLine(p.First(x => x.Name == "Kunafa"), 1), new CartLine(p.First(x => x.Name == "Mineral Water"), 2)],
                db.Customers("0569876543").Single().Id, "Cash", 100m);
            db.CompleteSale([new CartLine(p.First(x => x.Name == "Karak Tea"), 3), new CartLine(p.First(x => x.Name == "Croissant"), 2)], null, "Card", 0);
            using (var receipt = Invoice.Render(sale, db.GetSettings(), "Noura Al-Qahtani")) receipt.Save(Path.Combine(dir, "receipt.png"));
            using var form = new MainForm(db) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
            form.Show();
            foreach (var (page, file) in new[] { ("Sale  البيع", "sale.png"), ("Inventory  المخزون", "inventory.png"), ("Reports  التقارير", "reports.png") })
            {
                form.Show(page);
                if (page.StartsWith("Sale")) // put a few items in the cart so the screenshot shows a real order
                    foreach (var b in AllControls(form).OfType<Button>().Where(b => b.Text.StartsWith("Cappuccino") || b.Text.StartsWith("Date Cake")).ToList()) b.PerformClick();
                Application.DoEvents();
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                bmp.Save(Path.Combine(dir, file));
            }
            form.Close();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        return 0;
    }

    private static IEnumerable<Control> AllControls(Control root) => root.Controls.Cast<Control>().SelectMany(c => AllControls(c).Prepend(c));
}
