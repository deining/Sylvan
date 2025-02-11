﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;

namespace Sylvan.Data;

sealed class SimpleSchemaSerializer
{
	internal static readonly SimpleSchemaSerializer SingleLine = new SimpleSchemaSerializer(false);
	internal static readonly SimpleSchemaSerializer MultiLine = new SimpleSchemaSerializer(true);

	const string SeriesSymbol = "*";

	static readonly Regex ColSpecRegex =
		new Regex(
			@"^((?<BaseName>[^\>]+)\>)?(?<Name>[^\:]+)?(?::(?<Type>[a-z0-9]+)(\[(?<Size>\d+)\])?(?<AllowNull>\?)?(\{(?<Format>[^\}]+)\})?)?$",
			RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
		);

	static readonly Regex SeriesFormatRegex =
		new Regex("^(?<prefix>.*){{(Date|Integer)}}(?<suffix>.*)$");

	static readonly Regex NewLineRegex =
		new Regex(
			"\r\n|\n",
			RegexOptions.Multiline | RegexOptions.Compiled
		);


	static readonly Lazy<Dictionary<string, DbType>> ColumnTypeMap = new Lazy<Dictionary<string, DbType>>(InitializeTypeMap);

	static Dictionary<string, DbType> InitializeTypeMap()
	{
		var map = new Dictionary<string, DbType>(StringComparer.OrdinalIgnoreCase);
		var values = (DbType[])Enum.GetValues(typeof(DbType));
		foreach (DbType type in values)
		{
			map.Add(type.ToString(), type);
		}
		map.Add("bool", DbType.Boolean);
		map.Add("short", DbType.Int16);
		map.Add("int", DbType.Int32);
		map.Add("integer", DbType.Int32);
		map.Add("long", DbType.Int64);
		map.Add("float", DbType.Single);
		return map;
	}

	bool multiLine;

	internal SimpleSchemaSerializer(bool multiLine)
	{
		this.multiLine = multiLine;
	}

	/// <summary>
	/// Attempts to parse a schema specification.
	/// </summary>
	/// <param name="spec">The schema specification string.</param>
	/// <returns>A Schema, or null if it failed to parse.</returns>
	public Schema Parse(string spec)
	{
		var builder = new Schema.Builder();

		var map = ColumnTypeMap.Value;
		var colSpecs = NewLineRegex.Replace(spec, "").Split(',');

		foreach (var colSpec in colSpecs)
		{
			var match = ColSpecRegex.Match(colSpec);
			if (match.Success)
			{
				var typeGroup = match.Groups["Type"];
				var formatGroup = match.Groups["Format"];
				var baseNameGroup = match.Groups["BaseName"];
				var baseName = baseNameGroup.Success ? baseNameGroup.Value : null;
				var name = match.Groups["Name"].Value;
				DbType type = DbType.String;
				bool allowNull = false;
				int size = -1;
				if (typeGroup.Success)
				{
					var typeName = typeGroup.Value;
					allowNull = match.Groups["AllowNull"].Success;
					var sg = match.Groups["Size"];
					size = sg.Success ? int.Parse(sg.Value) : -1;
					if (!map.TryGetValue(typeName, out type))
					{
						throw new ArgumentException();
					}
				}
				string? format = null;
				if (formatGroup.Success)
				{
					format = formatGroup.Value;
				}

				var cb = new Schema.Column.Builder(name, type, allowNull)
				{
					BaseColumnName = baseName,
					ColumnSize = size == -1 ? null : (int?)size,
					Format = format
				};

				// if the column represents a series.
				if (name.EndsWith(SeriesSymbol))
				{
					cb.IsSeries = true;
					cb.ColumnName = "";
					cb.SeriesHeaderFormat = cb.BaseColumnName;
					cb.SeriesOrdinal = 0;
					cb.SeriesName = name.Substring(0, name.Length - 1);
					if (cb.BaseColumnName != null)
					{
						var m = DataBinder.SeriesKeyRegex.Match(cb.BaseColumnName);
						if (m.Success)
						{
							var seriesTypeName = m.Groups[1].Value;
							if (ColumnTypeMap.Value.TryGetValue(seriesTypeName, out DbType t))
							{
								cb.SeriesType = DataBinder.GetDataType(t);
							}
							else
							{
								throw new ArgumentException();
							}
						}
					}
				}

				builder.Add(cb);
			}
			else
			{
				throw new ArgumentException();
			}
		}
		return builder.Build();
	}

	/// <summary>
	/// Gets the specification string for this schema.
	/// </summary>
	/// <returns>A string.</returns>
	public string GetSchemaSpec(Schema schema)
	{
		var w = new StringWriter();
		bool first = true;
		foreach (var col in schema)
		{
			if (first)
			{
				first = false;
			}
			else
			{
				w.Write(",");
				if (multiLine)
				{
					w.WriteLine();
				}
			}

			if (col.IsSeries == true)
			{
				if (col.SeriesHeaderFormat != null)
				{
					w.Write(col.SeriesHeaderFormat);
					w.Write(">");
				}
				w.Write(col.SeriesName + "*");
			}
			else
			{
				if (col.BaseColumnName != null && col.BaseColumnName != col.ColumnName)
				{
					w.Write(col.BaseColumnName);
					w.Write(">");
				}
				w.Write(col.ColumnName);
			}
			WriteType(w, col);
		}

		return w.ToString();
	}

	static void WriteType(TextWriter w, Schema.Column col)
	{
		if (col.DataType == typeof(string) && col.AllowDBNull == false && col.ColumnSize == null)
			return;

		var typeName = col.CommonDataType switch
		{
			DbType.String => "string",
			DbType.Int32 => "int",
			DbType.Double => "double",
			DbType.Decimal => "decimal",
			DbType.Boolean => "bool",
			_ => null
		};
		if (typeName == null)
		{
			typeName = col.DataType?.Name;
		}
		if (typeName != null)
		{
			w.Write(":");

			w.Write(typeName);

			if (col.CommonDataType != null && HasLength(col.CommonDataType.Value))
			{
				if (col.ColumnSize != null)
				{
					w.Write("[");
					w.Write(col.ColumnSize?.ToString() ?? "*");
					w.Write("]");
				}
			}

			if (col.AllowDBNull != false)
			{
				w.Write("?");
			}

			if (col.Format != null)
			{
				w.Write("{");
				w.Write(col.Format);
				w.Write("}");
			}
		}
	}

	static bool HasLength(DbType type)
	{
		return
			type == DbType.String ||
			type == DbType.AnsiString ||
			type == DbType.Binary;
	}
}
