using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace Utils
{

    /// <summary>
    /// A data-reader style interface for reading Csv (and otherwise-char-separated) files.
    /// </summary>
    public class CsvReader : IDisposable
    {

        #region Private variables

        private Stream stream;
        private StreamReader reader;
        private char separator;
        private bool removeLiteralExcelFormulasField = false;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new Csv reader not associated with a stream, just for using ParseCsvLine.
        /// </summary>
        public CsvReader() : this(',') { }

        /// <summary>
        /// Creates a new Csv reader not associated with a stream, just for using ParseCsvLine.
        /// </summary>
        /// <param name="separator">The field separator character</param>
        public CsvReader(char separator)
        {
            this.separator = separator;
        }

        /// <summary>
        /// Creates a new Csv reader for the given stream.
        /// </summary>
        /// <param name="s">The stream to read the CSV from.</param>
        public CsvReader(Stream s) : this(s, null, ',') { }

        /// <summary>
        /// Creates a new reader for the given stream and separator.
        /// </summary>
        /// <param name="s">The stream to read the separator from.</param>
        /// <param name="separator">The field separator character</param>
        public CsvReader(Stream s, char separator) : this(s, null, separator) { }

        /// <summary>
        /// Creates a new Csv reader for the given stream and encoding.
        /// </summary>
        /// <param name="s">The stream to read the CSV from.</param>
        /// <param name="enc">The encoding used.</param>
        public CsvReader(Stream s, Encoding enc) : this(s, enc, ',') { }

        /// <summary>
        /// Creates a new reader for the given stream, encoding and separator character.
        /// </summary>
        /// <param name="s">The stream to read the data from.</param>
        /// <param name="enc">The encoding used.</param>
        /// <param name="separator">The separator character between the fields</param>
        public CsvReader(Stream s, Encoding enc, char separator)
        {

            this.separator = separator;
            this.stream = s;
            if (!s.CanRead)
            {
                throw new CsvReaderException("Could not read the given data stream!");
            }
            reader = (enc != null) ? new StreamReader(s, enc) : new StreamReader(s);
        }

        /// <summary>
        /// Creates a new Csv reader for the given text file path.
        /// </summary>
        /// <param name="filename">The name of the file to be read.</param>
        public CsvReader(string filename) : this(filename, null, ',') { }

        /// <summary>
        /// Creates a new reader for the given text file path and separator character.
        /// </summary>
        /// <param name="filename">The name of the file to be read.</param>
        /// <param name="separator">The field separator character</param>
        public CsvReader(string filename, char separator) : this(filename, null, separator) { }

        /// <summary>
        /// Creates a new Csv reader for the given text file path and encoding.
        /// </summary>
        /// <param name="filename">The name of the file to be read.</param>
        /// <param name="enc">The encoding used.</param>
        public CsvReader(string filename, Encoding enc)
            : this(filename, enc, ',') { }

        /// <summary>
        /// Creates a new reader for the given text file path, encoding and field separator.
        /// </summary>
        /// <param name="filename">The name of the file to be read.</param>
        /// <param name="enc">The encoding used.</param>
        /// <param name="separator">The field separator character.</param>
        public CsvReader(string filename, Encoding enc, char separator)
            : this(new FileStream(filename, FileMode.Open), enc, separator) { }

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

        ///<summary>Strip literal formulas created by <see cref="CsvWriter.PreserveLeadingZeroesForExcel" /></summary>
        ///<example>="0123" becomes 0123</example>
        public bool RemoveLiteralExcelFormulas
        {
            get { return removeLiteralExcelFormulasField; }
            set { removeLiteralExcelFormulasField = value; }
        }

        #endregion

        #region Parsing

        /// <summary>
        /// Reads the lines.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<CsvRow> ReadLinesAsCsvRows()
        {
            foreach (string[] fields in ReadLines())
            {
                yield return new CsvRow(fields);
            }
        }

        /// <summary>
        /// Reads the lines.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string[]> ReadLines()
        {
            if (reader == null)
            {
                Debug.WriteLine("CsvReader: reader is null");
                yield break;
            }

            while (!reader.EndOfStream)
            {
                string data = reader.ReadLine();
                if (data == null)
                {
                    Debug.WriteLine("CsvReader: data for line is null");
                    yield break;
                }
                if (data.Length == 0)
                {
                    Debug.WriteLine("CsvReader: data length is 0");
                    yield return new string[0];
                }
                else
                    yield return ParseCsvLine(data);
            }
        }

        /// <summary>
        /// Returns the fields for the next row of data (or null if at eof)
        /// </summary>
        /// <returns>A string array of fields or null if at the end of file.</returns>
        public string[] GetCsvLine()
        {
            return GetCsvLine(reader);
        }

        /// <summary>
        /// Returns the fields for the next row of data in the specified reader (or null if at eof)
        /// </summary>
        /// <returns>A string array of fields or null if at the end of file.</returns>
        public string[] GetCsvLine(StreamReader streamReader)
        {
            if (streamReader == null)
                throw new CsvReaderException("Reader is null");

            string line = streamReader.ReadLine();
            if (line == null) return null;
            if (line.Length == 0) return new string[0];

            string data = line;
            while (!ContainsEvenNumberOfQuotes(data))
            {
                line = streamReader.ReadLine();
                if(line == null) break;
                data += Environment.NewLine + line;
            }
            return ParseCsvLine(data);
        }

        private static bool ContainsEvenNumberOfQuotes(string s)
        {
            string[] parts = s.Split('\"');
            return (parts.Length % 2) == 1; // An odd number of parts indicates an even number of quotes.
        }

        /// <summary>
        /// Returns the fields for the specified string
        /// </summary>
        /// <param name="data">The Csv string to parse</param>
        /// <returns>A string array of fields</returns>
        public string[] ParseCsvLine(string data)
        {

            List<string> result = new List<string>();
            ParseCsvFields(result, data);
            return result.ToArray();
        }

        // Parses the fields and pushes the fields into the result arraylist
        private void ParseCsvFields(List<string> result, string data)
        {
            if (data == null) return;
            int pos = -1;
            while (pos < data.Length)
                result.Add(RemoveLiteralFormulas(ParseCsvField(data, ref pos)));
        }

        private string RemoveLiteralFormulas(string s)
        {
            if(RemoveLiteralExcelFormulas && s.StartsWith("=\"") && (s.IndexOf("\"", 2) == s.Length - 1)) // make sure there are no other quotes in between
            {
                s = s.Substring(2, s.Length - 3);
            }
            return s;
        }

        // Parses the field at the given position of the data, modified pos to match
        // the first unparsed position and returns the parsed field
        private string ParseCsvField(string data, ref int startSeparatorPosition)
        {

            if (startSeparatorPosition == data.Length - 1)
            {
                startSeparatorPosition++;
                // The last field is empty
                return "";
            }

            int fromPos = startSeparatorPosition + 1;

            // Determine if this is a quoted field
            if (data[fromPos] == '"')
            {
                // If we're at the end of the string, let's consider this a field that
                // only contains the quote
                if (fromPos == data.Length - 1)
                {
                    fromPos++;
                    return "\"";
                }

                // Otherwise, return a string of appropriate length with double quotes collapsed
                // Note that FSQ returns data.Length if no single quote was found
                int nextSingleQuote = FindSingleQuote(data, fromPos + 1);
                startSeparatorPosition = nextSingleQuote + 1;
                return data.Substring(fromPos + 1, nextSingleQuote - fromPos - 1).Replace("\"\"", "\"");
            }

            // The field ends in the next separator or EOL
            int nextSeparator = data.IndexOf(separator, fromPos);
            if (nextSeparator == -1)
            {
                startSeparatorPosition = data.Length;
                return data.Substring(fromPos);
            }
            else
            {
                startSeparatorPosition = nextSeparator;
                return data.Substring(fromPos, nextSeparator - fromPos);
            }
        }

        // Returns the index of the next single quote mark in the string 
        // (starting from startFrom)
        private static int FindSingleQuote(string data, int startFrom)
        {

            int i = startFrom - 1;
            while (++i < data.Length)
                if (data[i] == '"')
                {
                    // If this is a double quote, bypass the chars
                    if (i < data.Length - 1 && data[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }
                    else
                        return i;
                }
            // If no quote found, return the end value of i (data.Length)
            return i;
        }

        #endregion


        /// <summary>
        /// Disposes the reader. The underlying stream is closed.
        /// </summary>
        public void Dispose()
        {
            // Closing the reader closes the underlying stream, too
            if (reader != null) reader.Close();
            else if (stream != null)
                stream.Close(); // In case we failed before the reader was constructed
            GC.SuppressFinalize(this);
        }
    }

    #region CsvRow

    /// <summary></summary>
    public class CsvRow : IEnumerable<CsvField>
    {
        private readonly string[] innerFields;
        private Dictionary<int, CsvField> fields = new Dictionary<int, CsvField>();

        internal CsvRow(string[] fields)
        {
            innerFields = fields;
            if ( innerFields == null )
                innerFields = new string[0];
        }

        /// <summary>
        /// Gets the <see cref="Utils.CsvField"/> at the specified index.
        /// </summary>
        /// <value></value>
        public CsvField this[int index]
        {
            get
            {
                CsvField field;
                if (!fields.TryGetValue(index, out field))
                {
                    field = new CsvField(innerFields[index], index);
                    fields.Add(index, field);
                }

                return field;
            }
        }

        /// <summary>
        /// Gets the count of fields.
        /// </summary>
        /// <value>The count.</value>
        public int Count
        {
            get
            {
                return innerFields.Length;
            }
        }

        #region IEnumerable<CsvField> Members

        ///<summary>
        ///Returns an enumerator that iterates through the collection.
        ///</summary>
        ///
        ///<returns>
        ///A <see cref="T:System.Collections.Generic.IEnumerator`1"></see> that can be used to iterate through the collection.
        ///</returns>
        ///<filterpriority>1</filterpriority>
        public IEnumerator<CsvField> GetEnumerator()
        {
            for (int i = 0; i < innerFields.Length; i++)
                yield return this[i];
        }

        #endregion

        #region IEnumerable Members

        ///<summary>
        ///Returns an enumerator that iterates through a collection.
        ///</summary>
        ///
        ///<returns>
        ///An <see cref="T:System.Collections.IEnumerator"></see> object that can be used to iterate through the collection.
        ///</returns>
        ///<filterpriority>2</filterpriority>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }

    #endregion
    
    /// <summary></summary>
    public class CsvField
    {
        private readonly string field;
        private readonly int index;

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvField"/> class.
        /// </summary>
        /// <param name="field">The field.</param>
        /// <param name="index"></param>
        protected internal CsvField(string field, int index)
        {
            this.field = field;
            this.index = index;
        }

        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <value>The value.</value>
        public string Value
        {
            get
            {
                return field;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the Value is null.
        /// </summary>
        /// <value><c>true</c> if the Value is null; otherwise, <c>false</c>.</value>
        public bool IsNull
        {
            get
            {
                return Value == null;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the Value is null or empty.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if the Value is null or empty; otherwise, <c>false</c>.
        /// </value>
        public bool IsNullOrEmpty
        {
            get
            {
                return string.IsNullOrEmpty(Value.Trim());
            }
        }

        /// <summary>
        /// Gets the index of the current field within the CsvRow
        /// </summary>
        /// <value>The index.</value>
        public int Index
        {
            get { return index; }
        }

        /// <summary>
        /// Gets the value as a decimal.
        /// </summary>
        /// <returns></returns>
        public decimal GetDecimal()
        {
            if (IsNullOrEmpty) return decimal.Zero;

            decimal retVal;

            string value = Value.Replace("$", string.Empty);

            if (!decimal.TryParse(value, out retVal))
                throw new FormatException(string.Format("The field at index {0} is not in decimal format", Index));

            return retVal;
        }

        /// <summary>
        /// Gets the int32.
        /// </summary>
        /// <returns></returns>
        public int GetInt32()
        {
            if (IsNullOrEmpty) return 0;

            int retVal;
            if (!int.TryParse(Value, out retVal))
                throw new FormatException(string.Format("The field at index {0} is not in int format", Index));

            return retVal;
        }
    }


    /// <summary>
    /// Exception class for CsvReader exceptions.
    /// </summary>
    [Serializable]
    public class CsvReaderException : ApplicationException
    {

        /// <summary>
        /// Constructs a new CsvReaderException.
        /// </summary>
        public CsvReaderException() : this("The CSV Reader encountered an error.") { }

        /// <summary>
        /// Constructs a new exception with the given message.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public CsvReaderException(string message) : base(message) { }

        /// <summary>
        /// Constructs a new exception with the given message and the inner exception.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="inner">Inner exception that caused this issue.</param>
        public CsvReaderException(string message, Exception inner) : base(message, inner) { }

        /// <summary>
        /// Constructs a new exception with the given serialization information.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected CsvReaderException(System.Runtime.Serialization.SerializationInfo info,
                                     System.Runtime.Serialization.StreamingContext context)
            : base(info, context) { }

    }

}
