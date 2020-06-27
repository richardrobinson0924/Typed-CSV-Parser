using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace CSVParserTests
{
    public struct Foo
    {
        public int A { get; }
        public int? B { get; set; }
        [CSV.PropertyName("z")] public int C { get; set; }
        public IEnumerable<int> D { get; set; }
    }

    public class Tests
    {
        [Test]
        public void NoSetterTest()
        {
            const string s = "A,B,z\n1,2,3";
            using var reader = new StringReader(s);
            
            var parser = new CSV.Parser<Foo>(reader);
            Assert.AreEqual(0, parser.ReadLine().A);
        }

        [Test]
        public void RegularPropTest()
        {
            const string s = "A,B,z\n1,2,3";
            using var reader = new StringReader(s);

            var parser = new CSV.Parser<Foo>(reader);
            Assert.AreEqual(2, parser.ReadLine().B);
        }
        
        [Test]
        public void AttributeTest()
        {
            const string s = "A,B,z\n1,2,3";
            using (var reader = new StringReader(s))
            {
                var parser = new CSV.Parser<Foo>(reader);
                Assert.AreEqual(3, parser.ReadLine().C);
            }

            const string s2 = "A,B,C\n1,2,3";
            using (var reader = new StringReader(s2))
            {
                var parser2 = new CSV.Parser<Foo>(reader);
                Assert.AreEqual(0, parser2.ReadLine().C);
            }
        }

        [Test]
        public void CustomTypeNoParserTest()
        {
            const string s = "A,B,z,D\n1,2,3,1 2 3 4";
            using var reader = new StringReader(s);

            var parser = new CSV.Parser<Foo>(reader);
            Assert.Catch<CSV.ParseException>(() => parser.ReadLine());
        }
        
        [Test]
        public void CustomTypeBadParserTest()
        {
            const string s = "A,B,z,D\n1,2,3,1 2 3 4";
            using var reader = new StringReader(s);

            var parser = new CSV.Parser<Foo>(reader);
            CSV.Parsers.RegisterParser<IEnumerable<int>>(_ => throw new NotImplementedException());
            
            Assert.Catch<CSV.ParseException>(() => parser.ReadLine());
        }
        
        [Test]
        public void CustomTypeParserTest()
        {
            const string s = "A,B,z,D\n1,2,3,1 2 3 4";
            using var reader = new StringReader(s);
            var parser = new CSV.Parser<Foo>(reader);
            
            CSV.Parsers.RegisterParser(str => str.Split(" ").Select(CSV.Parsers.Parse<int>));

            var enumerable = (IEnumerable<int>) new List<int> {1, 2, 3, 4};
            Assert.AreEqual(enumerable, parser.ReadLine().D);
        }

        [Test]
        public void MissingFieldTest()
        {
            const string s = "A,B,z\n1,,3";
            using (var reader = new StringReader(s))
            {
                var parser = new CSV.Parser<Foo>(reader);
                Assert.AreEqual(null, parser.ReadLine().B);
            }

            const string s2 = "A,B,z\n1,2";
            using (var reader = new StringReader(s2))
            {
                var parser2 = new CSV.Parser<Foo>(reader);
                Assert.AreEqual(0, parser2.ReadLine().C);
            }
        }

        [Test]
        public void SkipRowsTest()
        {
            const string s = "garbage\nA,B,z\n1,2,3";
            using var reader = new StringReader(s);
            
            var parser = new CSV.Parser<Foo>(reader, initiallySkippedRows: 1);
            Assert.AreEqual(3, parser.ReadLine().C);
        }

        [Test]
        public void IterationTest()
        {
            const string s = "B,z\n1,2\n4,5";
            using var reader = new StringReader(s);

            var parser = new CSV.Parser<Foo>(reader);

            var list = new List<Foo>
            {
                new Foo {B = 1, C = 2},
                new Foo {B = 4, C = 5}
            };

            foreach (var (actual, expected) in parser.Zip(list))
            {
                Assert.AreEqual(expected, actual);
            }
        }
    }
}