###

  SparkleShare is teh awezome!

  Be sure to edit the CoffeeScript file; it generates the JavaScript.

  You can generate the JavaScript from CoffeeScript by running:
    coffee -c --bare .

  You can generate the JS file automagically when you save by running this:
    coffee -w -c --bare .

  You can test the event viewer in your browser by running:
    python -m SimpleHTTPServer
  ...and browsing to http://localhost:8000/events.html

###

(($) ->

  $.ajaxSetup
    isLocal: true
    dataType: 'json'


  class ChangeSet
    @ajax
    @changes = {}
    @repo
    
    constructor: (@repo = 'all') ->
      @update()

    update: ->
      @ajax = $.getJSON @buildFileName(), (data) => @changes = data

    buildFileName: ->
      "spec.json"

    render: ->
      _compileTemplate = =>
        template = Handlebars.compile $('#changeset-template').html()        
        $('#content').html template @changes

      ### 
      * Either template it now, or when a pending ajax request finishes
      ###
      if @ajax?.readyState
        @ajax.complete -> _compileTemplate()
      else
        _compileTemplate()


  changes = new ChangeSet
  changes.render()

  return

)(jQuery)
