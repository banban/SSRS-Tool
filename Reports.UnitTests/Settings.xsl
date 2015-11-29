<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
	<xsl:output method="html"/>

	<xsl:template match="/Settings">
		<html>
			<head>
				<TITLE>Unit Test Results Report</TITLE>
				<link rel="stylesheet" type="text/css" href="settings.css" />
			</head>
			<body>
				<h1>Unit Test Results:</h1>
				<xsl:apply-templates select="ReportServer"/>
				<br/>Run at:<xsl:value-of select="@RunAt"/>
			</body>
		</html>
	</xsl:template>

	<xsl:template match="ReportServer">
		<div class="ReportServer">
			Report Server:
			<xsl:element name='a'>
				<xsl:attribute name='href'>
					<xsl:value-of select="@Path"/>
				</xsl:attribute>
				<xsl:value-of select="@Path"/>
			</xsl:element> (<xsl:value-of select="@Mode"/> mode)
		</div>
		<xsl:apply-templates select="Folder"/>
	</xsl:template>

	<xsl:template match="Folder">
		<div class="Folder">
			Folder: <xsl:value-of select="@Name"/>
		</div>
		<xsl:apply-templates select="Report"/>
	</xsl:template>

	<xsl:template match="Report">
		<div class="Report">
			Report: <xsl:value-of select="@Name"/>
		</div>
		<xsl:apply-templates select="Params/Param"/>
		<xsl:apply-templates select="TestCases/TestCase"/>
	</xsl:template>

	<xsl:template match="Param">
		<div class="Param">
			Param Name=<xsl:value-of select="@Name"/>; Value=<xsl:value-of select="@Value"/>
		</div>
	</xsl:template>

	<xsl:template match="TestCase">
		<xsl:element name="div">
			<xsl:choose>
				<xsl:when test="@Passed='True'">
					<xsl:attribute name="class">TestCasePassed</xsl:attribute>
					Passed
				</xsl:when>
				<xsl:otherwise>
					<xsl:attribute name="class">TestCaseFailed</xsl:attribute>
					Failed
				</xsl:otherwise>
			</xsl:choose>
			Test Case: Path=<xsl:value-of select="@Path"/>; Assertion=<xsl:value-of select="@Assert"/>;
			<xsl:choose>
				<xsl:when test="@Passed='True'">
					Passed
				</xsl:when>
				<xsl:otherwise>
					Failed
				</xsl:otherwise>
			</xsl:choose>
		</xsl:element>
	</xsl:template>

</xsl:stylesheet>