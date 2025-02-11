using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Sylvan.Data.Csv;

public class CsvDataWriterTests
{
	// use \n for newlines to make assertions easier
	static CsvDataWriterOptions TestOptions = new CsvDataWriterOptions { NewLine = "\n" };

	// a culture that uses ',' for numeric decimal separator
	static CultureInfo ItalianCulture = CultureInfo.GetCultureInfoByIetfLanguageTag("it-IT");

	static string GetCsv<T>(IEnumerable<T> data) where T : class
	{
		var dr = data.AsDataReader();
		return GetCsv(dr);
	}

	static string GetCsv(DbDataReader dr, CsvDataWriterOptions opts = null)
	{
		var sw = new StringWriter();
		var csv = CsvDataWriter.Create(sw, opts?? TestOptions);
		csv.Write(dr);
		return sw.ToString();
	}

	[Fact]
	public void VariableColumn()
	{
		var csvData = "a,b,c\n1,2,3\n1,2,3,4\n1,2\n1\n,,,,5\n";
		var r = 
			CsvDataReader.Create(new StringReader(csvData))
			.AsVariableField(r => r.RowFieldCount);
		var sw = new StringWriter();
		var cw = CsvDataWriter.Create(sw, new CsvDataWriterOptions { NewLine = "\n" });
		cw.Write(r);

		var str = sw.ToString();
		Assert.Equal(csvData, str);
	}

	[Fact]
	public void Simple()
	{
		var data = new[]
			{
				new
				{
					Boolean = true,
					Integer = int.MaxValue,
					Double = 15.25,
					Date = new DateTime(2020, 01, 01),
					Text = "Abcd",
				}
			};

		var csv = GetCsv(data);

		var r = CsvDataReader.Create(new StringReader(csv));
		r.Read();
		var expected = data[0];
		Assert.Equal(expected.Boolean, r.GetBoolean(0));
		Assert.Equal(expected.Integer, r.GetInt32(1));
		Assert.Equal(expected.Double, r.GetDouble(2));
		Assert.Equal(expected.Date, r.GetDateTime(3));
		Assert.Equal(expected.Text, r.GetString(4));
	}

	[Fact]
	public void BinaryBase64()
	{
		var data =
			new[] {
				 new {
					  Name = "A",
					  Value = new byte[] { 1, 2, 3, 4, 5 },
				}
			};

		var csv = GetCsv(data);
		Assert.Equal("Name,Value\nA,AQIDBAU=\n", csv);
	}

	[Fact]
	public void BinaryHex()
	{
		var data =
			new[] {
				 new {
					  Name = "A",
					  Value = new byte[] { 1, 2, 3, 4, 5, 15, 16 },
				}
			};

		var opt = 
			new CsvDataWriterOptions { 
				BinaryEncoding = BinaryEncoding.Hexadecimal,
				NewLine = "\n"
			};

		var csv = GetCsv(data.AsDataReader(), opt);
		Assert.Equal("Name,Value\nA,01020304050f10\n", csv);
	}

	[Fact]
	public void BinaryBase64Big()
	{
		BinaryBig(BinaryEncoding.Base64);
	}

	[Fact]
	public void BinaryHexBig()
	{
		BinaryBig(BinaryEncoding.Hexadecimal);
	}

	void BinaryBig(BinaryEncoding encoding)
	{
		var bytes = Enumerable.Range(0, 0x1800).Select(i => (byte)i).ToArray();
		var data =
			new[] {
				 new {
					  Name = "A",
					  Value = bytes,
					  Value2 = bytes,
				}
			};

		var opt =
			new CsvDataWriterOptions
			{
				BinaryEncoding = encoding,
				NewLine = "\n",
				Delimiter = '\t',
			};

		var csv = GetCsv(data.AsDataReader(), opt);

		var readerOpts = 
			new CsvDataReaderOptions { 
				BufferSize = 0x10000, 
				BinaryEncoding = encoding 
			};

		var csvr = CsvDataReader.Create(new StringReader(csv), readerOpts);
		Assert.True(csvr.Read());
		Assert.Equal(bytes, csvr.GetBytes(1));
		Assert.Equal(bytes, csvr.GetBytes(2));
	}

	[Fact]
	public void WriteDate()
	{
		var data = new[]
			{
				new { Date = new DateTime(2021, 2, 6, 0, 0, 0, DateTimeKind.Local) },
				new { Date = new DateTime(2021, 2, 6, 1, 2, 3, DateTimeKind.Local) },
				new { Date = new DateTime(2021, 2, 6, 0, 0, 0, DateTimeKind.Utc) },
				new { Date = new DateTime(2021, 2, 6, 1, 2, 3, DateTimeKind.Utc) },
				new { Date = new DateTime(2021, 2, 6, 0, 0, 0, DateTimeKind.Unspecified) },
				new { Date = new DateTime(2021, 2, 6, 1, 2, 3, DateTimeKind.Unspecified) },
			};

		var csvStr = GetCsv(data);
		var csv = CsvDataReader.Create(new StringReader(csvStr));
		var idx = 0;
		while (csv.Read())
		{
			var expected = data[idx];
			var result = csv.GetDateTime(0);
			Assert.Equal(expected.Date.ToUniversalTime(), result.ToUniversalTime());
			idx++;
		}
	}

	[Fact]
	public void WriteDateTimeOffset()
	{
		var offset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2021, 2, 6));
		var offset1H = offset.Add(TimeSpan.FromHours(1));
		var data = new[]
			{
				new { Date = new DateTimeOffset(2021, 2, 6, 0, 0, 0, offset) },
				new { Date = new DateTimeOffset(2021, 2, 6, 1, 2, 3, offset ) },
				new { Date = new DateTimeOffset(2021, 2, 6, 0, 0, 0, TimeSpan.Zero ) },
				new { Date = new DateTimeOffset(2021, 2, 6, 1, 2, 3, TimeSpan.Zero ) },
				new { Date = new DateTimeOffset(2021, 2, 6, 0, 0, 0, offset1H ) },
				new { Date = new DateTimeOffset(2021, 2, 6, 1, 2, 3, offset1H ) },
			};

		var csvStr = GetCsv(data);
		var csv = CsvDataReader.Create(new StringReader(csvStr));
		var idx = 0;
		while (csv.Read())
		{
			var expected = data[idx];
			var result = csv.GetDateTimeOffset(0);
			Assert.Equal(expected.Date, result);
			idx++;
		}
	}

	[Fact]
	public void EscapedStyle1()
	{
		var opts = new CsvDataWriterOptions { Style = CsvStyle.Escaped, Escape = '\\', NewLine = "\n" };
		var sw = new StringWriter();
		var w = CsvDataWriter.Create(sw, opts);

		var data = new[]
			{
				new
				{
					Name = "Value with comma, and \r\n newline.",
					Value = 12,
				},
				new
				{
					Name = "#Comment",
					Value = 16,
				},
			};

		w.Write(data.AsDataReader());
		var str = sw.ToString();
		Assert.Equal("Name,Value\nValue with comma\\, and \\\r\n newline.,12\n\\#Comment,16\n", str);
	}


	[Fact]
	public void EscapedStyle2()
	{
		var opts = new CsvDataWriterOptions { Style = CsvStyle.Escaped, Escape = '\\', NewLine = "\n" };
		var sw = new StringWriter();
		var w = CsvDataWriter.Create(sw, opts);

		var data = new[]
			{
				new
				{
					Name = "Value with comma, and \r\n newline.",
					Value = 12,
				},
				new
				{
					Name = "#Comment",
					Value = 16,
				},
			};

		w.Write(data.AsDataReader());
		var str = sw.ToString();
		Assert.Equal("Name,Value\nValue with comma\\, and \\\r\n newline.,12\n\\#Comment,16\n", str);
	}

	[Fact]
	public void WriteQuote()
	{
		var data = new[]
			{
				new
				{
					Text = "Test, 1",
				},
				new
				{
					Text = "\"test2\"",
				},
			};

		var csv = GetCsv(data);
		Assert.Equal("Text\n\"Test, 1\"\n\"\"\"test2\"\"\"\n", csv);
	}

	[Fact]
	public void CultureCommaDecimalPoint()
	{
		var sw = new StringWriter();
		var csv = CsvDataWriter.Create(sw, new CsvDataWriterOptions { NewLine = "\n", Culture = ItalianCulture });

		var dr = new[]
		{
			new { Value = 12.34 },
		};

		csv.Write(dr.AsDataReader());
		var str = sw.ToString();
		Assert.Equal("Value\n\"12,34\"\n", str);
	}

	[Fact]
	public void CultureCommaDecimalPoint2()
	{
		var sw = new StringWriter();
		var csv = CsvDataWriter.Create(sw, new CsvDataWriterOptions { NewLine = "\n", Culture = ItalianCulture, Delimiter = ';' });

		var dr = new[]
		{
			new { Name= "A", Value = 12.34 },
		};

		csv.Write(dr.AsDataReader());
		var str = sw.ToString();
		Assert.Equal("Name;Value\nA;12,34\n", str);
	}

	[Fact]
	public void Comment1()
	{
		var sw = new StringWriter();
		var csv = CsvDataWriter.Create(sw, TestOptions);

		var dr = new[]
		{
			new { Name = "#1", Value = "#2" },
		};

		csv.Write(dr.AsDataReader());
		var str = sw.ToString();
		Assert.Equal("Name,Value\n\"#1\",#2\n", str);
	}


	[Fact]
	public void BufferSpanBugDateTime()
	{
		var date = new DateTime(2000, 1, 1);
		BufferSpanBug(i => date.AddDays(i), dr => dr.GetDateTime(1));
	}

	[Fact]
	public void BufferSpanBugInt32()
	{
		BufferSpanBug(i => i + 1000, dr => dr.GetInt32(1));
	}

	[Fact]
	public void BufferSpanBugInt64()
	{
		BufferSpanBug(i => i + 1000L, dr => dr.GetInt64(1));
	}

	[Fact]
	public void BufferSpanBugFloat()
	{
		BufferSpanBug(i => 1.5f * i, dr => dr.GetFloat(1));
	}

	[Fact]
	public void BufferSpanBugDouble()
	{
		BufferSpanBug(i => 1.125 * i, dr => dr.GetDouble(1));
	}

	[Fact]
	public void BufferSpanBugGuid()
	{
		BufferSpanBug(i => Guid.NewGuid(), dr => dr.GetGuid(1));
	}

	void BufferSpanBug<T>(Func<int, T> allocator, Func<DbDataReader, T> selector)
	{
		// There was a bug where values that spanned buffers wouldn't be written at all
		const int RecordCount = 10000;
		var sw = new StringWriter();
		var csv = CsvDataWriter.Create(sw, TestOptions);

		var data =
			Enumerable
			.Range(0, RecordCount)
			.Select(i => new { Id = i, Value = allocator(i) })
			.ToArray();

		csv.Write(data.AsDataReader());
		var str = sw.ToString();
		var reader = new StringReader(str);
		var csvReader = CsvDataReader.Create(reader);

		int c = 0;
		while (csvReader.Read())
		{
			var i = csvReader.GetInt32(0);
			Assert.Equal(c, i);
			var d = selector(csvReader);
			Assert.Equal(data[i].Value, d);
			c++;
		}
		Assert.Equal(RecordCount, c);
	}


	[Fact]
	public void CsvWriteBatches()
	{
		using var tw = File.CreateText("output.csv");
		var data = "A,B,C\n1,2,3\n4,5,6\n";

		{
			var r = CsvDataReader.Create(new StringReader(data));
			var csvWriter = CsvDataWriter.Create(tw);
			csvWriter.Write(r);
		}

		{
			var r = CsvDataReader.Create(new StringReader(data));
			var csvWriter = CsvDataWriter.Create(tw, new CsvDataWriterOptions { WriteHeaders = false });
			csvWriter.Write(r);
		}
	}
}
