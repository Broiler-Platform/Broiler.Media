using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Broiler.Media.Metadata;

namespace Broiler.Media.Tests;

internal static class XmpMetadataTests
{
    private const long Ceiling = 1024 * 1024;

    public static void Register(List<(string Name, Func<ValueTask> Body)> tests)
    {
        tests.Add(("XMP reads properties written as child elements", ReadsPropertiesWrittenAsChildElements));
        tests.Add(("XMP reads properties written as description attributes", ReadsPropertiesWrittenAsDescriptionAttributes));
        tests.Add(("XMP reads simple property that skips container", ReadsSimplePropertyThatSkipsContainer));
        tests.Add(("XMP reads packet that has no xmpmeta wrapper", ReadsPacketThatHasNoXmpmetaWrapper));
        tests.Add(("XMP prefers default language alternative", PrefersDefaultLanguageAlternative));
    }

    private static ValueTask ReadsPropertiesWrittenAsChildElements()
    {
        XmpReadResult result = XmpReader.Read(Packet(
            """
            <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Quarterly Report</rdf:li></rdf:Alt></dc:title>
            <dc:creator><rdf:Seq><rdf:li>Ada Lovelace</rdf:li><rdf:li>Grace Hopper</rdf:li></rdf:Seq></dc:creator>
            <dc:description><rdf:Alt><rdf:li xml:lang="x-default">Numbers for the quarter</rdf:li></rdf:Alt></dc:description>
            <dc:subject><rdf:Bag><rdf:li>finance</rdf:li><rdf:li>quarterly</rdf:li></rdf:Bag></dc:subject>
            <dc:language><rdf:Bag><rdf:li>en-GB</rdf:li></rdf:Bag></dc:language>
            <xmp:CreatorTool>Broiler.Writer</xmp:CreatorTool>
            <xmp:CreateDate>2026-09-01T09:30:00Z</xmp:CreateDate>
            <pdf:Producer>Broiler.Documents.Pdf</pdf:Producer>
            """), Ceiling);

        Assert.Equal(XmpReadOutcome.Read, result.Outcome);
        XmpMetadata metadata = result.Metadata;

        Assert.Equal("Quarterly Report", metadata.Title);
        Assert.Equal(2, metadata.Authors.Count);
        Assert.Equal("Ada Lovelace", metadata.Authors[0]);
        Assert.Equal("Grace Hopper", metadata.Authors[1]);
        Assert.Equal("Numbers for the quarter", metadata.Description);
        Assert.Equal(2, metadata.Keywords.Count);
        Assert.Equal("finance", metadata.Keywords[0]);
        Assert.Equal("quarterly", metadata.Keywords[1]);
        Assert.Equal("en-GB", metadata.Language);
        Assert.Equal("Broiler.Writer", metadata.CreatorTool);
        Assert.Equal("Broiler.Documents.Pdf", metadata.Producer);
        Assert.Equal(8, metadata.FieldCount);
        return ValueTask.CompletedTask;
    }

    private static ValueTask ReadsPropertiesWrittenAsDescriptionAttributes()
    {
        XmpReadResult result = XmpReader.Read(
            Latin1(Wrap("""<rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:xmp="http://ns.adobe.com/xap/1.0/" xmlns:pdf="http://ns.adobe.com/pdf/1.3/" dc:title="Attribute Title" xmp:CreatorTool="Some Tool" pdf:Producer="Some Producer"/>""")),
            Ceiling);

        Assert.Equal(XmpReadOutcome.Read, result.Outcome);
        Assert.Equal("Attribute Title", result.Metadata.Title);
        Assert.Equal("Some Tool", result.Metadata.CreatorTool);
        Assert.Equal("Some Producer", result.Metadata.Producer);
        return ValueTask.CompletedTask;
    }

    private static ValueTask ReadsSimplePropertyThatSkipsContainer()
    {
        XmpReadResult result = XmpReader.Read(Packet("<dc:title>Bare Title</dc:title>"), Ceiling);
        Assert.Equal("Bare Title", result.Metadata.Title);
        return ValueTask.CompletedTask;
    }

    private static ValueTask ReadsPacketThatHasNoXmpmetaWrapper()
    {
        XmpReadResult result = XmpReader.Read(
            Latin1($"""
            <rdf:RDF xmlns:rdf="{XmpReader.RdfNamespace}">
              <rdf:Description rdf:about="" xmlns:dc="{XmpReader.DublinCoreNamespace}">
                <dc:title>Unwrapped</dc:title>
              </rdf:Description>
            </rdf:RDF>
            """),
            Ceiling);

        Assert.Equal("Unwrapped", result.Metadata.Title);
        return ValueTask.CompletedTask;
    }

    private static ValueTask PrefersDefaultLanguageAlternative()
    {
        XmpReadResult result = XmpReader.Read(Packet(
            """
            <dc:title><rdf:Alt>
              <rdf:li xml:lang="de-DE">Titel</rdf:li>
              <rdf:li xml:lang="x-default">Title</rdf:li>
              <rdf:li xml:lang="fr-FR">Titre</rdf:li>
            </rdf:Alt></dc:title>
            """), Ceiling);

        Assert.Equal("Title", result.Metadata.Title);
        return ValueTask.CompletedTask;
    }

    private static byte[] Packet(string inner) =>
        Latin1(Wrap($"""
            <rdf:Description rdf:about=""
              xmlns:dc="{XmpReader.DublinCoreNamespace}"
              xmlns:xmp="{XmpReader.XmpBasicNamespace}"
              xmlns:pdf="{XmpReader.AdobePdfNamespace}">
              {inner}
            </rdf:Description>
            """));

    private static string Wrap(string rdfBody) =>
        $"""
        <?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="{XmpReader.RdfNamespace}">
            {rdfBody}
          </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);
}
