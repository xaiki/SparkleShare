SUBDIRS =	\
	Linux/Nautilus \
	Mac

ASSEMBLY = SparkleShare
TARGET = exe

LINK = $(REF_SPARKLESHARE) $(NOTIFY_SHARP_LIBS) $(WEBKIT_SHARP_LIBS)

if HAVE_APP_INDICATOR
BUILD_DEFINES="-define:HAVE_APP_INDICATOR"
endif

SOURCES =  \
	Program.cs \
	Linux/SparkleAbout.cs \
	Linux/SparkleBubbles.cs \
	Linux/SparkleController.cs \
	Linux/SparkleEntry.cs \
	Linux/SparkleEventLog.cs \
	Linux/SparkleSetup.cs \
	Linux/SparkleSetupWindow.cs \
	Linux/SparkleStatusIcon.cs \
	Linux/SparkleSpinner.cs \
	Linux/SparkleUI.cs \
	Linux/SparkleUIHelpers.cs \
	SparkleAboutController.cs \
	SparkleBubblesController.cs \
	SparkleControllerBase.cs \
	SparkleEventLogController.cs \
	SparkleExtensions.cs \
	SparkleOptions.cs \
	SparkleInvite.cs \
	SparklePlugin.cs \
	SparkleSetupController.cs \
	SparkleStatusIconController.cs

include $(top_srcdir)/build/build.mk

bin_SCRIPTS = sparkleshare

Applicationsdir = $(datadir)/applications
dist_Applications_DATA = Linux/sparkleshare.desktop
