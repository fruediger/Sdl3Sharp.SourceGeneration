using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Sdl3Sharp.SourceGeneration;

partial class FormattedConstantSourceGenerator
{
	private const string FormattedConstantAttributeNamespaceName = $"{nameof(Sdl3Sharp)}.{nameof(SourceGeneration)}";

	private const string FormatCultureTypeName = "FormatCulture";
	private const string FormatCultureDefaultMemberName = "Default";
	private const string FormatCultureInvariantMemberName = "Invariant";
	private const string FormatCultureCurrentCultureMemberName = "CurrentCulture";
	private const string FormatCultureCurrentUICultureMemberName = "CurrentUICulture";

	private const string FormattedConstantAttributeTypeName = "FormattedConstantAttribute";
	private const string FormattedConstantAttributeFormatPropertyName = "Format";
	private const string FormattedConstantAttributeArgsPropertyName = "Args";
	private const string FormattedConstantAttributeCulturePropertyName = "Culture";

	private const string GeneratedAttributeOutputFileName = $"{nameof(Sdl3Sharp)}.{nameof(SourceGeneration)}.FormattedConstantAttribute.g.cs";

	private static void GenerateFormattedConstantAttributeSource(IncrementalGeneratorPostInitializationContext pic)
	{
		var formatParameterName = FormattedConstantAttributeFormatPropertyName switch
		{
			[var head, ..var tail] => $"{char.ToLowerInvariant(head)}{tail}",
			_ => string.Empty
		};

		var argsParameterName = FormattedConstantAttributeArgsPropertyName switch
		{
			[var head, ..var tail] => $"{char.ToLowerInvariant(head)}{tail}",
			_ => string.Empty
		};

		pic.AddSource(GeneratedAttributeOutputFileName, SourceText.From(
			text: $$"""
				#nullable enable

				namespace {{FormattedConstantAttributeNamespaceName}};

				/// <summary>Specifies the culture to use when formatting a <see cref="{{FormattedConstantAttributeTypeName}}">formatted constant</see></summary>
				[global::Microsoft.CodeAnalysis.EmbeddedAttribute]
				internal enum {{FormatCultureTypeName}}
				{
					/// <summary>Uses the default formatting behavior of <see cref="string.Format(string, object?[])"/> without specifying a culture</summary>
					{{FormatCultureDefaultMemberName}} = default,

					/// <summary>Uses <see cref="global::System.Globalization.CultureInfo.InvariantCulture"/> for culture-independent formatting</summary>
					{{FormatCultureInvariantMemberName}},

					/*
					/// <summary>Uses <see cref="global::System.Globalization.CultureInfo.CurrentCulture"/> for formatting based on the current thread's culture</summary>
					/// <remarks>
					/// Note: This uses the culture of the machine running the source generator (at compile time), not the runtime culture.
					/// For most cases, <see cref="{{FormatCultureInvariantMemberName}}"/> is preferred for consistent results.
					/// </remarks>
					{{FormatCultureCurrentCultureMemberName}},

					/// <summary>Uses <see cref="global::System.Globalization.CultureInfo.CurrentUICulture"/> for formatting based on the current thread's UI culture</summary>
					/// <remarks>
					/// Note: This uses the UI culture of the machine running the source generator (at compile time), not the runtime culture.
					/// For most cases, <see cref="{{FormatCultureInvariantMemberName}}"/> is preferred for consistent results. 
					/// </remarks>
					{{FormatCultureCurrentUICultureMemberName}}
					*/
				}

				/// <summary>Indicates that the attributed static partial method or property returns a compile-time formatted constant string</summary>
				/// <param name="{{formatParameterName}}">The format string, using the same syntax as <see cref="string.Format(string, object?[])"/></param>
				/// <param name="{{argsParameterName}}">The arguments to substitute into the format string placeholders</param>
				/// <remarks>
				/// <para>
				/// The source generator calls <see cref="string.Format(string, object?[])"/> (or an overload with a culture-specific
				/// <see cref="global::System.IFormatProvider"/> if <see cref="{{FormattedConstantAttributeCulturePropertyName}}"/> is set) at compile time
				/// and generates an implementation that returns the result as a literal constant.
				/// </para>
				/// <para>
				/// The target must be a <c>static partial</c> method or a <c>static partial</c> property with only a <c>get</c> accessor. 
				/// Supported return types are: 
				/// <list type="bullet">
				/// <item><term><see cref="string">string</see> or <see cref="string">string</see>?</term><description>Returns a string literal or <see cref="string.Empty"/></description></item>
				/// <item><term><see cref="global::System.ReadOnlySpan{T}">ReadOnlySpan</see>&lt;<see cref="char">char</see>&gt;</term><description>Returns a string literal or <c>[]</c></description></item>
				/// <item><term><see cref="global::System.ReadOnlySpan{T}">ReadOnlySpan</see>&lt;<see cref="byte">byte</see>&gt;</term><description>Returns a UTF-8 string literal or <c>[]</c></description></item>
				/// </list>
				/// </para>
				/// </remarks>
				/// <example>
				/// <code>
				/// [FormattedConstant("Hello, {0}!", "World")]
				/// static partial string GetGreeting(); // Returns "Hello, World!"
				/// 
				/// [FormattedConstant("{0:N2}", 1234.5, Culture = FormatCulture.Invariant)]
				/// static partial ReadOnlySpan&lt;byte&gt; GetFormattedNumber(); // Returns "1,234.50"u8
				/// 
				/// [FormattedConstant("{0}", nameof(MyClass))]
				/// static partial ReadOnlySpan&lt;char&gt; GetClassName(); // Returns "MyClass"
				/// </code>
				/// </example>
				[global::Microsoft.CodeAnalysis.EmbeddedAttribute]
				[global::System.AttributeUsage(global::System.AttributeTargets.Method | global::System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
				internal sealed class {{FormattedConstantAttributeTypeName}}(string {{formatParameterName}}, params object?[] {{argsParameterName}}) : global::System.Attribute
				{
					/// <summary>Gets the format string</summary>
					/// <value>The format string using the same syntax as <see cref="string.Format(string, object?[])"/></value>
					public string {{FormattedConstantAttributeFormatPropertyName}} { get; } = {{formatParameterName}};

					/// <summary>Gets the arguments to substitute into the format string</summary>
					/// <value>The arguments for the format string placeholders</value>
					public object?[] {{FormattedConstantAttributeArgsPropertyName}} { get; } = {{argsParameterName}};

					/// <summary>Gets or initializes the culture to use for formatting</summary>
					/// <value>The culture to use.  Defaults to <see cref="{{FormatCultureTypeName}}.{{FormatCultureDefaultMemberName}}"/></value>
					public {{FormatCultureTypeName}} {{FormattedConstantAttributeCulturePropertyName}} { get; init; } = {{FormatCultureTypeName}}.{{FormatCultureDefaultMemberName}};
				}

				#nullable restore
				""",
			encoding: Encoding.UTF8
		));
	}
}