dist_plugins_in_files = \
    bitbucket.xml.in \
    github.xml.in \
    gitorious.xml.in \
    gnome.xml.in \
    ssnet.xml.in \
    own-server.xml.in \
    slcfk.xml.in

dist_plugins_DATA = \
    $(dist_plugins_in_files:.xml.in=.xml) \
    github.png \
    gitorious.png \
    bitbucket.png \
    gnome.png \
    ssnet.png \
    own-server.png \
	  slcfk.png

#@INTLTOOL_XML_RULE@

pluginsdir = $(pkgdatadir)/plugins/

EXTRA_DIST=$(dist_plugins_in_files) $(xml_DATA)

MAINTAINERCLEANFILES = \
	Makefile.in
