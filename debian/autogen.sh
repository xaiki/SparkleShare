#!/usr/bin/env bash

function error () {
	echo "Error: $1" 1>&2
	exit 1
}

function run () {
	echo "Running $@ ..."
	$@ 2>.autogen.log || {
		cat .autogen.log 1>&2
		rm .autogen.log
		error "Could not run $1, which is required to configure $PROJECT"
	}
	rm .autogen.log
}

if [ $(pkg-config --modversion gnome-doc-utils 2> /dev/null) ]; then
    run gnome-doc-prepare --automake --force
else
    echo "gnome-doc-utils not found; user help will not be built"
    echo "AC_DEFUN([GNOME_DOC_INIT], [AC_MSG_NOTICE([])])" > build/m4/gnome-doc-utils.m4
    ACLOCAL_FLAGS="-I build/m4 $ACLOCAL_FLAGS"
    touch gnome-doc-utils.make
fi

if git --help &>/dev/null; then
	git submodule update --init
fi

run intltoolize --force --copy
run libtoolize --force --copy --automake
run aclocal -I build/m4/sparkleshare -I build/m4/shamrock -I build/m4/shave $ACLOCAL_FLAGS
run autoconf

run automake --gnu --add-missing --force --copy \
	-Wno-portability -Wno-portability

if test -d $srcdir/SmartIrc4net; then
    echo Running SmartIrc4net/autogen.sh ...
    (cd $srcdir/SmartIrc4net; NOCONFIGURE=1 ./autogen.sh "$@")
    echo Done running SmartIrc4net/autogen.sh ...
fi

exit $?
