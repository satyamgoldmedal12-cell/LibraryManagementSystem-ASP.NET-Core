using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using SkiaSharp;
namespace LibraryManagement.Infrastructure.Reporting;

public sealed class ReportExporter : IReportExporter
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    public ReportFile Export(ReportResult result,string format,CancellationToken ct)
    {
        var name=$"library-{result.Definition.Key}-{result.Totals.GeneratedAt:yyyyMMdd-HHmmss}";
        return format switch
        {
            "excel" => new(Excel(result,ct),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",name+".xlsx"),
            "pdf" => new(Pdf(result,ct),"application/pdf",name+".pdf"),
            _ => throw new BusinessException("Choose Excel or PDF export.")
        };
    }
    private static string Filters(ReportResult r) => $"{r.Definition.DateBasis}: {r.Filter.FromDate?.ToString("yyyy-MM-dd")??"Any"} to {r.Filter.ToDate?.ToString("yyyy-MM-dd")??"Any"}; member record: {r.Filter.MemberId?.ToString()??"All"}; book record: {r.Filter.BookId?.ToString()??"All"}; status: {r.Filter.Status}";
    private static string Summary(ReportResult r) => r.Filter.Report=="stock" ? $"Titles: {r.Totals.TotalRecords:N0} | Copies: {r.Totals.TotalCopies:N0} | Available: {r.Totals.AvailableCopies:N0}" : $"Rows: {r.Totals.TotalRecords:N0} | Issues: {r.Totals.IssuedCount:N0} | Returns: {r.Totals.ReturnedCount:N0} | Collected (INR): {r.Totals.CollectedAmount.ToString("N2",Culture)}";
    private static string Clean(string text)
    {
        var result=new StringBuilder();
        foreach(var rune in text.EnumerateRunes())
        {
            var n=rune.Value;
            if(n is 9 or 10 or 13 || n is >=0x20 and <=0xD7FF || n is >=0xE000 and <=0xFFFD || n is >=0x10000 and <=0x10FFFF) result.Append(rune.ToString());
        }
        return result.ToString();
    }
    private static byte[] Excel(ReportResult report,CancellationToken ct)
    {
        using var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            void Xml(string name,Action<XmlWriter> content)
            {
                using var stream=zip.CreateEntry(name,CompressionLevel.Fastest).Open();
                using var writer=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=false});
                content(writer);
            }
            void Raw(string name,string value){using var stream=zip.CreateEntry(name).Open();using var writer=new StreamWriter(stream,new UTF8Encoding(false));writer.Write(value);}
            Raw("[Content_Types].xml","""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Raw("_rels/.rels","""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Raw("xl/workbook.xml","""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Library Report" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Raw("xl/_rels/workbook.xml.rels","""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Raw("xl/styles.xml","""<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="3"><numFmt numFmtId="164" formatCode="yyyy-mm-dd"/><numFmt numFmtId="165" formatCode="yyyy-mm-dd hh:mm:ss"/><numFmt numFmtId="166" formatCode="mmm yyyy"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFDCEDE8"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="7"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" applyAlignment="1"><alignment wrapText="1" vertical="top"/></xf><xf numFmtId="0" fontId="1" fillId="2" borderId="0" applyAlignment="1"><alignment wrapText="1" vertical="top"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" applyNumberFormat="1"/><xf numFmtId="4" fontId="0" fillId="0" borderId="0" applyNumberFormat="1"/><xf numFmtId="166" fontId="0" fillId="0" borderId="0" applyNumberFormat="1"/><xf numFmtId="3" fontId="0" fillId="0" borderId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");
            Xml("xl/worksheets/sheet1.xml",w=>
            {
                const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                w.WriteStartElement("worksheet",ns);
                void Start(string n)=>w.WriteStartElement(n,ns);
                void Attr(string n,object v)=>w.WriteAttributeString(n,Convert.ToString(v,Culture));
                Start("sheetViews");Start("sheetView");Attr("workbookViewId",0);Start("pane");Attr("ySplit",5);Attr("topLeftCell","A6");Attr("activePane","bottomLeft");Attr("state","frozen");w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();
                Start("cols");for(var i=0;i<report.Definition.Columns.Count;i++){Start("col");Attr("min",i+1);Attr("max",i+1);Attr("width",report.Definition.Columns[i].Width);Attr("customWidth",1);w.WriteEndElement();}w.WriteEndElement();
                Start("sheetData");
                void Cell(int col,int row,ReportCell cell,bool header=false)
                {
                    Start("c");Attr("r",Column(col)+row);
                    var style=header?1:cell.Type switch{ReportValueType.Date=>2,ReportValueType.Timestamp=>3,ReportValueType.Money=>4,ReportValueType.Month=>5,ReportValueType.Number=>6,_=>0};Attr("s",style);
                    if(cell.Number.HasValue){Start("v");w.WriteString(cell.Number.Value.ToString(Culture));w.WriteEndElement();}
                    else if(cell.Date.HasValue){Start("v");w.WriteString(cell.Date.Value.ToOADate().ToString(Culture));w.WriteEndElement();}
                    else{Attr("t","inlineStr");Start("is");Start("t");w.WriteAttributeString("xml","space","http://www.w3.org/XML/1998/namespace","preserve");w.WriteString(Clean(cell.Text));w.WriteEndElement();w.WriteEndElement();}
                    w.WriteEndElement();
                }
                void Meta(int row,string text){Start("row");Attr("r",row);Attr("ht",row==4?32:20);Attr("customHeight",1);Cell(0,row,new(text,ReportValueType.Text));w.WriteEndElement();}
                Meta(1,report.Definition.Title);Meta(2,Filters(report));Meta(3,$"Generated {report.Totals.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC. All matching rows; not only the screen page.");Meta(4,report.Definition.Notes);
                Start("row");Attr("r",5);Attr("ht",32);Attr("customHeight",1);for(var i=0;i<report.Definition.Columns.Count;i++)Cell(i,5,new(report.Definition.Columns[i].Heading,ReportValueType.Text),true);w.WriteEndElement();
                var index=6;
                foreach(var row in report.Rows){ct.ThrowIfCancellationRequested();Start("row");Attr("r",index);for(var i=0;i<row.Count;i++)Cell(i,index,row[i]);w.WriteEndElement();index++;}
                Meta(index+1,Summary(report));w.WriteEndElement();
                Start("autoFilter");Attr("ref",$"A5:{Column(report.Definition.Columns.Count-1)}{Math.Max(5,index-1)}");w.WriteEndElement();
                Start("mergeCells");Attr("count",4);for(var row=1;row<=4;row++){Start("mergeCell");Attr("ref",$"A{row}:{Column(report.Definition.Columns.Count-1)}{row}");w.WriteEndElement();}w.WriteEndElement();
                w.WriteEndElement();
            });
        }
        return output.ToArray();
    }
    private static string Column(int zeroBased)
    {
        var value=zeroBased+1;var result="";while(value>0){value--;result=(char)('A'+value%26)+result;value/=26;}return result;
    }
    private static byte[] Pdf(ReportResult report,CancellationToken ct)
    {
        using var output=new MemoryStream();
        using var document=SKDocument.CreatePdf(output);
        using var typeface=SKTypeface.FromFamilyName("Arial");
        using var font=new SKFont(typeface,9);
        using var heading=new SKFont(typeface,17);
        using var ink=new SKPaint{Color=SKColor.Parse("243B38"),IsAntialias=true};
        using var muted=new SKPaint{Color=SKColor.Parse("536663"),IsAntialias=true};
        using var fill=new SKPaint{Color=SKColor.Parse("E4EFEC")};
        using var line=new SKPaint{Color=SKColor.Parse("D5DEDB"),StrokeWidth=.5f};
        float width=report.Definition.Columns.Count>9?1191:842,height=report.Definition.Columns.Count>9?842:595,margin=30;
        var weights=report.Definition.Columns.Select(c=>(float)c.Width).ToArray();var widths=weights.Select(v=>(width-margin*2)*v/weights.Sum()).ToArray();
        SKCanvas? canvas=null;float y=0;var page=0;
        List<string> Wrap(string value,SKFont useFont,float maxWidth)
        {
            var lines=new List<string>();var current="";
            foreach(var rune in Clean(value).EnumerateRunes())
            {
                if(rune.Value==13)continue;
                if(rune.Value==10){lines.Add(current);current="";continue;}
                var next=current+rune;
                if(current.Length>0&&useFont.MeasureText(next)>maxWidth){lines.Add(current);current=rune.ToString();}else current=next;
            }
            lines.Add(current);return lines;
        }
        void TextBlock(string text,SKFont useFont,SKPaint paint)
        {
            foreach(var textLine in Wrap(text,useFont,width-margin*2)){canvas!.DrawText(textLine,margin,y,SKTextAlign.Left,useFont,paint);y+=useFont.Size+4;}
        }
        void EndPage(){canvas!.DrawText($"Library Desk | UTC | Page {page}",margin,height-15,SKTextAlign.Left,font,muted);document.EndPage();}
        void BeginPage()
        {
            page++;canvas=document.BeginPage(width,height);canvas.Clear(SKColors.White);y=margin+17;
            TextBlock(report.Definition.Title,heading,ink);TextBlock(Filters(report),font,muted);TextBlock($"Generated {report.Totals.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC | {Summary(report)}",font,muted);
            if(page==1)TextBlock(report.Definition.Notes,font,muted);
            y+=8;var x=margin;var headers=report.Definition.Columns.Select((c,i)=>Wrap(c.Heading,font,widths[i]-10)).ToArray();var headerHeight=headers.Max(h=>h.Count)*13+12;
            canvas.DrawRect(margin,y,width-margin*2,headerHeight,fill);
            for(var i=0;i<headers.Length;i++){for(var j=0;j<headers[i].Count;j++)canvas.DrawText(headers[i][j],x+5,y+14+j*13,SKTextAlign.Left,font,ink);x+=widths[i];}y+=headerHeight;
        }
        BeginPage();
        if(report.Rows.Count==0){y+=22;TextBlock("No records match these filters.",font,muted);}
        foreach(var row in report.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var cells=row.Select((c,i)=>Wrap(c.Text,font,widths[i]-10)).ToArray();var rowHeight=Math.Max(25,cells.Max(c=>c.Count)*13+12);
            if(y+rowHeight>height-35){EndPage();BeginPage();}
            var x=margin;
            for(var i=0;i<cells.Length;i++){for(var j=0;j<cells[i].Count;j++)canvas!.DrawText(cells[i][j],x+5,y+14+j*13,SKTextAlign.Left,font,ink);x+=widths[i];}
            y+=rowHeight;canvas!.DrawLine(margin,y,width-margin,y,line);
        }
        EndPage();document.Close();return output.ToArray();
    }
}
