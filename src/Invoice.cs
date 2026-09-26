using System.Drawing.Printing;
using QRCoder;

namespace ZatcaPos;

/// <summary>Draws an 80 mm bilingual simplified tax invoice with the ZATCA QR code, for the printer or a preview image.</summary>
public static class Invoice
{
    private const int Width = 300; // hundredths of an inch ≈ 76 mm printable width on 80 mm rolls

    public static Bitmap QrImage(string payload, int pixelsPerModule = 4)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        byte[] png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        using var ms = new MemoryStream(png);
        return new Bitmap(ms);
    }

    /// <summary>Renders at 100 units per inch (printer units); returns the height used.</summary>
    public static int Draw(Graphics g, Sale sale, Settings shop, string? customer)
    {
        using var title = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var body = new Font("Segoe UI", 7.5f);
        using var bold = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var big = new Font("Segoe UI", 10f, FontStyle.Bold);
        var center = new StringFormat { Alignment = StringAlignment.Center };
        var right = new StringFormat { Alignment = StringAlignment.Far };
        var rtl = new StringFormat(StringFormatFlags.DirectionRightToLeft) { Alignment = StringAlignment.Near };
        float y = 4;

        void Center(string s, Font f) { g.DrawString(s, f, Brushes.Black, new RectangleF(0, y, Width, 40), center); y += f.GetHeight(g) + 1; }
        // Label on the left, value on the right; when both do not fit on one line the value moves to the next line.
        void Row(string left, string rightText, Font f)
        {
            float lw = g.MeasureString(left, f).Width, rw = g.MeasureString(rightText, f).Width;
            g.DrawString(left, f, Brushes.Black, 0, y);
            if (left.Length > 0 && lw + rw + 6 > Width) y += f.GetHeight(g);
            g.DrawString(rightText, f, Brushes.Black, new RectangleF(0, y, Width, 30), right);
            y += f.GetHeight(g) + 1;
        }
        void Rule() { y += 2; g.DrawLine(Pens.Black, 0, y, Width, y); y += 4; }

        Center(shop.SellerNameAr, title);
        Center(shop.SellerName, big);
        Center(shop.Address, body);
        Center($"VAT No. {shop.VatNumber}  الرقم الضريبي", body);
        y += 2;
        Center("فاتورة ضريبية مبسطة", bold);
        Center("Simplified Tax Invoice", bold);
        Rule();
        var local = sale.IssuedUtc.ToLocalTime();
        Row("Invoice  رقم الفاتورة", sale.Number, body);
        Row("Date  التاريخ", local.ToString("yyyy-MM-dd HH:mm"), body);
        Row("Cashier  الكاشير", sale.Cashier, body);
        if (customer != null) Row("Customer  العميل", customer, body);
        Rule();
        Row("Item  الصنف", "Qty × Price = Amount", bold);
        foreach (var l in sale.Lines)
        {
            g.DrawString(l.Name, body, Brushes.Black, 0, y);
            g.DrawString(l.NameAr, body, Brushes.Black, new RectangleF(0, y, Width, 20), rtl);
            y += body.GetHeight(g);
            Row("", $"{l.Qty} × {Zatca.Money(l.Price)} = {Zatca.Money(l.Net)}", body);
        }
        Rule();
        Row("Total excl. VAT  الإجمالي قبل الضريبة", Zatca.Money(sale.Net), body);
        Row("VAT 15%  ضريبة القيمة المضافة", Zatca.Money(sale.Vat), body);
        Row("Total incl. VAT", $"SAR {Zatca.Money(sale.Total)}", big);
        Row("الإجمالي شامل الضريبة", "", body);
        Row($"Paid ({sale.Payment})  المدفوع", Zatca.Money(sale.Tendered), body);
        if (sale.Payment == "Cash") Row("Change  الباقي", Zatca.Money(sale.Tendered - sale.Total), body);
        Rule();
        using (var qr = QrImage(sale.Qr))
        {
            const int size = 120;
            g.DrawImage(qr, (Width - size) / 2, y, size, size);
            y += size + 4;
        }
        Center("Scan with the ZATCA VAT app to verify", body);
        Center($"UUID {sale.Uuid[..8]}…   Hash {sale.Hash[..10]}…", body);
        Center("Thank you  شكراً لزيارتكم", bold);
        return (int)Math.Ceiling(y + 6);
    }

    public static PrintDocument Document(Sale sale, Settings shop, string? customer)
    {
        var doc = new PrintDocument { DocumentName = sale.Number };
        doc.DefaultPageSettings.PaperSize = new PaperSize("Receipt 80mm", 315, 1200);
        doc.DefaultPageSettings.Margins = new Margins(8, 8, 8, 8);
        doc.PrintPage += (_, e) =>
        {
            e.Graphics!.TranslateTransform(e.MarginBounds.Left, e.MarginBounds.Top);
            Draw(e.Graphics, sale, shop, customer);
            e.HasMorePages = false;
        };
        return doc;
    }

    /// <summary>Pixel image of the receipt (2x scale) for on-screen preview, sharing or README screenshots.</summary>
    public static Bitmap Render(Sale sale, Settings shop, string? customer)
    {
        const float scale = 2f;
        using var probe = new Bitmap(1, 1);
        int h;
        using (var pg = Graphics.FromImage(probe)) { pg.PageUnit = GraphicsUnit.Display; h = Draw(pg, sale, shop, customer); }
        var bmp = new Bitmap((int)((Width + 20) * scale), (int)((h + 20) * scale));
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(10, 10);
        g.PageUnit = GraphicsUnit.Display;
        Draw(g, sale, shop, customer);
        return bmp;
    }
}
