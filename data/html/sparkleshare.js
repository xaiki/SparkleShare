/*

  SparkleShare is teh awezome!

  Be sure to edit the CoffeeScript file; it generates the JavaScript.

  You can generate the JavaScript from CoffeeScript by running:
    coffee -c --bare .

  You can generate the JS file automagically when you save by running this:
    coffee -w -c --bare .

  You can test the event viewer in your browser by running:
    python -m SimpleHTTPServer
  ...and browsing to http://localhost:8000/events.html

*/var __bind = function(fn, me){ return function(){ return fn.apply(me, arguments); }; };
(function($) {
  var ChangeSet, changes;
  $.ajaxSetup({
    isLocal: true,
    dataType: 'json'
  });
  ChangeSet = (function() {
    ChangeSet.ajax;
    ChangeSet.changes = {};
    ChangeSet.repo;
    function ChangeSet(repo) {
      this.repo = repo != null ? repo : 'all';
      this.update();
    }
    ChangeSet.prototype.update = function() {
      return this.ajax = $.getJSON(this.buildFileName(), __bind(function(data) {
        return this.changes = data;
      }, this));
    };
    ChangeSet.prototype.buildFileName = function() {
      return "spec.json";
    };
    ChangeSet.prototype.render = function() {
      return this.ajax.success(__bind(function() {
        var template;
        template = Handlebars.compile($('#changeset-template').html());
        return $('#content').html(template(this.changes));
      }, this));
    };
    return ChangeSet;
  })();
  changes = new ChangeSet;
  changes.render();
})(jQuery);