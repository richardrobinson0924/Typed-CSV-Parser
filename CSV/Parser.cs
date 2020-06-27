using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CSV
{
    /// <inheritdoc />
    public sealed class ParseException : Exception
    {
        /// <inheritdoc />
        public ParseException(string message = "") : base(message)
        {
        }
        
        /// <inheritdoc />
        public ParseException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// This attribute may be applied to any property of a class or struct to indicate that the custom name should
    /// be matched against the headers of the CSV file instead of the name of the attribute
    /// </summary>
    /// 
    /// <example>
    /// <c>[CSV.PropertyName("value")] public int Num { get; set; }</c>
    /// </example>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PropertyNameAttribute : Attribute
    {
        /// <summary>
        /// The name of the property.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="PropertyNameAttribute"/> with the specified property name.
        /// </summary>
        /// <param name="name">The name of the property.</param>
        public PropertyNameAttribute(string name) => Name = name;
    }

    /// <summary>
    /// A struct for accessing the map of parsers and implementing custom parsers used by <see cref="Parser{TRow}"/>
    /// </summary>
    ///
    /// By default, all types supported by <see cref="Convert.ChangeType(object?,Type)"/> are supported.
    /// Parsers for other types can be made available via <see cref="RegisterParser{T}"/>
    public static class Parsers
    {
        private static readonly Dictionary<Type, Converter<string, dynamic>> Dict = new Dictionary<Type, Converter<string, dynamic>>();

        /// <summary>
        /// Globally registers a parser for <typeparamref name="T"/>, overriding any parser which may exist for the type.
        /// </summary>
        /// <param name="parser">a <c>Converter</c> from a string to an arbitrary type <c>T</c></param>
        /// <typeparam name="T">a type to make available for parsing into</typeparam>
        /// <exception cref="ArgumentException">if <paramref name="parser"/> is <see cref="Parse{T}"/></exception>
        public static void RegisterParser<T>(Converter<string, T> parser)
        {
            if (parser == Parse<T>)
            {
                throw new ArgumentException("parser cannot be `Parsers.Parse<T>`");
            }
            
            object CovarianceCaster(string s) => parser(s);
            Dict[typeof(T)] = CovarianceCaster;
        }

        /// <summary>
        /// Parses <paramref name="s"/> into <typeparamref name="T"/> if <c>T</c> is a supported type for parsing 
        /// </summary>
        public static T Parse<T>(string s) => TryParse(s, typeof(T));

        internal static dynamic TryParse(string s, Type t)
        {
            if (Dict.ContainsKey(t))
            {
                try
                {
                    return Dict[t].Invoke(s);
                }
                catch (Exception e)
                {
                    throw new ParseException($"The parser for {t} failed", e);
                }
            }

            try
            {
                var nonNullType = Nullable.GetUnderlyingType(t) ?? t;
                return s != "" ? Convert.ChangeType(s, nonNullType) : null;
            }
            catch
            {
                throw new ParseException($"There is no parser associated with {t}");
            }
        }
    }

    /// <summary>
    /// This class allows CSV text strings to be conveniently and easily parsed into an Enumerable sequence of objects of type <c>TRow</c>
    /// </summary>
    ///
    /// <para>
    /// By default, CSV.Parser supports parsing all types supported by <see cref="Convert.ChangeType(object?,Type)"/>
    /// (and their nullable counterparts if applicable). Parsers for other types may be added via
    /// <see cref="Parsers.RegisterParser{T}(Converter{string,T})"/>.
    /// </para>
    ///
    /// <example>
    /// Suppose there exists the following struct <c>Foo</c>:
    /// <code>
    /// public struct Foo
    /// {
    ///     [CSV.PropertyName("Value")] public float X { get; set; }
    ///     public string Name { get; set; }
    /// }
    /// </code>
    /// Given a <see cref="TextReader"/> whose contents are
    /// <code>
    /// Name,Value
    /// hello,3.14
    /// world
    /// </code>
    /// each line can be parsed into a <c>Foo</c> object using
    /// <code>
    /// var csv = new CSV.Parser(reader)
    /// foreach (var foo in csv) Console.WriteLine(foo);
    /// </code>
    /// </example>
    /// 
    /// <typeparam name="TRow">
    /// a struct type that satisfies the following properties:
    /// <list type="bullet">
    ///     <item>It has a no-argument constructor (satisfies the <c>new()</c> constraint)</item>
    ///     <item>Any property which should be affected should have an accessor</item>
    /// </list>
    /// </typeparam>
    public class Parser<TRow> : IEnumerable<TRow> where TRow : struct
    {
        private readonly TextReader _reader;
        private readonly string _delimiter;
        private readonly List<string> _headers;

        /// <summary>
        /// Creates a new CSV.Parser instance from the specified <c>reader</c> whose lines may be parsed into <c>TRow</c> instances
        /// </summary>
        /// <param name="reader">a <c>TextReader</c> containing N lines of text, each line containing M data fields
        /// separated by a <c>delimiter</c></param>
        /// <param name="delimiter">the delimiter to use</param>
        /// <param name="initiallySkippedRows">the number of rows to initially skip before processing <paramref name="reader"/>.
        /// Useful for possible inclusion of metadata at the beginning of the CSV data.</param>
        /// <exception cref="ArgumentException">The <paramref name="reader"/> is empty</exception>
        /// <remarks>It is the caller's responsibility to Dispose of <paramref name="reader"/>.</remarks>
        public Parser(TextReader reader, string delimiter = ",", int initiallySkippedRows = 0)
        {
            _reader = reader;
            _delimiter = delimiter;
            
            for (var i = 0; i < initiallySkippedRows; i++) { _reader.ReadLine(); }

            var line = reader.ReadLine();
            if (line == null)
            {
                throw new ArgumentException("The reader is empty");
            }
            
            _headers = line.Split(delimiter).ToList();
        }

        /// <summary>
        /// Parses the next line of the associated <see cref="TextReader"/> into a <c>TRow</c> object
        /// </summary>
        /// <returns>The parsed TRow object, or <c>new TRow()</c> if the line is empty
        /// or if there are no settable properties</returns>
        /// <exception cref="ParseException">There is no valid parser for one of the types of the fields of
        /// <typeparamref name="TRow"/>, or a parser threw an Exception while parsing</exception>
        public TRow ReadLine()
        {
            var line = _reader.ReadLine();
            if (line == null) return default;

            var split = line.Split(_delimiter);
            object row = new TRow();

            var settableProps = typeof(TRow).GetProperties().Where(p => p.CanWrite).ToList();
            foreach (var prop in settableProps)
            {
                var attr = prop.GetCustomAttribute<PropertyNameAttribute>();
                var name = attr == null ? prop.Name : attr.Name;

                var idx = _headers.IndexOf(name);
                if (idx >= split.Length) continue;

                var parsed = idx == -1 ? null : Parsers.TryParse(split[idx].Trim(' ', '\"'), prop.PropertyType);
                prop.SetValue(row, parsed);
            }

            return (TRow) row;
        }

        /// <summary>
        /// Returns an <see cref="IEnumerator{T}"/> by repeatedly invoking <see cref="Parser{TRow}.ReadLine()"/>.
        /// </summary>
        /// <returns>an <see cref="IEnumerator{T}"/> of all the parsed rows</returns>
        public IEnumerator<TRow> GetEnumerator()
        {
            for (var row = ReadLine(); !row.Equals(default(TRow)); row = ReadLine())
            {
                yield return row;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}