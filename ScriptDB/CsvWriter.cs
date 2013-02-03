using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Data;
using System.Text.RegularExpressions;
//using System.Web.UI.WebControls;
using System.Web;

namespace Utils
{
    /// <summary>
    /// A tool class for writing Csv and other char-separated field files.
    /// </summary>
    public class CsvWriter : StreamWriter
    {

        #region Private variables

        private char separator;
        private bool preserveLeadingZeroesForExcelField = false;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new Csv writer for the given filename (overwriting existing contents).
        /// </summary>
        /// <param name="filename">The name of the file being written to.</param>
        public CsvWriter(string filename)
            : this(filename, ',', false) { }

        /// <summary>
        /// Creates a new Csv writer for the given filename.
        /// </summary>
        /// <param name="filename">The name of the file being written to.</param>
        /// <param name="append">True if the contents shall be appended to the
        /// end of the possibly existing file.</param>
        public CsvWriter(string filename, bool append)
            : this(filename, ',', append) { }

        /// <summary>
        /// Creates a new Csv writer for the given filename and encoding.
        /// </summary>
        /// <param name="filename">The name of the file being written to.</param>
        /// <param name="enc">The encoding used.</param>
        /// <param name="append">True if the contents shall be appended to the
        /// end of the possibly existing file.</param>
        public CsvWriter(string filename, Encoding enc, bool append)
            : this(filename, enc, ',', append) { }

        /// <summary>
        /// Creates a new writer for the given filename and separator.
        /// </summary>
        /// <param name="filename">The name of the file being written to.</param>
        /// <param name="separator">The field separator character used.</param>
        /// <param name="append">True if the contents shall be appended to the
        /// end of the possibly existing file.</param>
        public CsvWriter(string filename, char separator, bool append)
            : base(filename, append) { this.separator = separator; }

        /// <summary>
        /// Creates a new writer for the given filename, separator and encoding.
        /// </summary>
        /// <param name="filename">The name of the file being written to.</param>
        /// <param name="enc">The encoding used.</param>
        /// <param name="separator">The field separator character used.</param>
        /// <param name="append">True if the contents shall be appended to the
        /// end of the possibly existing file.</param>
        public CsvWriter(string filename, Encoding enc, char separator, bool append)
            : base(filename, append, enc) { this.separator = separator; }

        /// <summary>
        /// Creates a new Csv writer for the given stream.
        /// </summary>
        /// <param name="s">The stream to write the CSV to.</param>
        public CsvWriter(Stream s)
            : this(s, ',') { }

        /// <summary>
        /// Creates a new writer for the given stream and separator character.
        /// </summary>
        /// <param name="s">The stream to write the CSV to.</param>
        /// <param name="separator">The field separator character used.</param>
        public CsvWriter(Stream s, char separator)
            : base(s) { this.separator = separator; }

        /// <summary>
        /// Creates a new writer for the given stream, separator and encoding.
        /// </summary>
        /// <param name="s">The stream to write the CSV to.</param>
        /// <param name="enc">The encoding used.</param>
        /// <param name="separator">The field separator character used.</param>
        public CsvWriter(Stream s, Encoding enc, char separator)
            : base(s, enc) { this.separator = separator; }

        #endregion

        #region Properties

        /// <summary>
        /// The separator character for the fields. Comma for normal CSV.
        /// </summary>
        public char Separator
        {
            get { return separator; }
            set { separator = value; }
        }

        ///<summary>Wrap numbers having leading zeroes in literal formulas</summary>
        ///<example>0123 becomes ="0123"</example>
        public bool PreserveLeadingZeroesForExcel
        {
            get { return preserveLeadingZeroesForExcelField;  }
            set { preserveLeadingZeroesForExcelField = value; }
        }

        #endregion

        /// <summary>
        /// Write the specified data to csv.
        /// </summary>
        /// <param name="content"></param>
        public void WriteFields(params object[] content)
        {

            string s;

            for (int i = 0; i < content.Length; ++i)
            {
                s = (content[i] != null ? content[i].ToString() : "");
                s = QuoteIfNecessary(s);
                Write(s);

                // Write the separator unless we're at the last position
                if (i < content.Length - 1)
                    Write(separator);
            }
            Write(NewLine);
        }

        private string QuoteIfNecessary(string s)
        {
            if (s.IndexOfAny(new char[] { Separator, '"', '\r', '\n' }) >= 0)
            {
                // We have to quote the string
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            else if(PreserveLeadingZeroesForExcel)
            {
                if(Regex.IsMatch(s, @"^\s*-?\s*0[0-9.]+\s*$"))
                {
                    s = string.Format("=\"{0}\"", s);
                }
            }
            return s;
        }

        /// <summary>
        /// Write the contents of the DataTable to csv.
        /// </summary>
        public void WriteDataTable(DataTable table)
        {
            WriteDataTable(table, true);
        }

        /// <summary>
        /// Write the contents of a DataTable to csv.
        /// </summary>
        public void WriteDataTable(DataTable table, bool includeHeader)
        {
            if (includeHeader)
            {
                DataColumn[] dataColumns = new DataColumn[table.Columns.Count];
                table.Columns.CopyTo(dataColumns, 0);
                WriteFields(dataColumns);
            }

            foreach (DataRow row in table.Rows)
            {
                WriteFields(row.ItemArray);
            }
        }

        ///// <summary>
        ///// Write the contents of a TableRow to csv.
        ///// </summary>
        //public void WriteTableRow(TableRow tableRow)
        //{
        //    // TODO: handle invisible columns... how do I know if a column is visible without access to the parent control?

        //    //TableCell[] cells = new TableCell[tableRow.Cells.Count];
        //    //tableRow.Cells.CopyTo(cells, 0);
        //    //WriteFields(cells);
        //    string[] cellText = new string[tableRow.Cells.Count];
        //    for (int i = 0; i < tableRow.Cells.Count; i++)
        //    {
        //        cellText[i] = tableRow.Cells[i].Text;
        //    }
        //    WriteFields(cellText);
        //}

        ///// <summary>
        ///// Write the contents of a GridView to csv.
        ///// </summary>
        //public void WriteGridView(GridView gridView)
        //{
        //    WriteGridView(gridView, true);
        //}

        ///// <summary>
        ///// Write the contents of a GridView to csv.
        ///// </summary>
        //public void WriteGridView(GridView gridView, bool includeHeader)
        //{
        //    List<int> visibleColumnIndexes = new List<int>();

        //    for (int i = 0; i < gridView.Columns.Count; i++)
        //    {
        //        if (gridView.Columns[i].Visible)
        //        {
        //            visibleColumnIndexes.Add(i);
        //        }
        //    }

        //    if (includeHeader)
        //    {

        //        string[] headerText = new string[visibleColumnIndexes.Count];
        //        for(int i = 0; i < visibleColumnIndexes.Count; i++)
        //        {
        //            headerText[i] = DecodeHtml(gridView.Columns[visibleColumnIndexes[i]].HeaderText);
        //        }
        //        WriteFields(headerText);
        //    }

        //    foreach (TableRow tableRow in gridView.Rows)
        //    {
        //        string[] cellText = new string[visibleColumnIndexes.Count];
        //        for (int i = 0; i < visibleColumnIndexes.Count; i++)
        //        {
        //            cellText[i] = DecodeHtml(tableRow.Cells[visibleColumnIndexes[i]].Text);
        //        }
        //        WriteFields(cellText);
        //    }
        //}

        //private string DecodeHtml(string s)
        //{
        //    const string NBSP = "&nbsp;";

        //    if (s == NBSP)
        //    {
        //        s = String.Empty;
        //    }
        //    else
        //    {
        //        s = s.Replace(NBSP, " ");
        //        s = HttpUtility.HtmlDecode(s);
        //    }
        //    return s;
        //}
    }
}
