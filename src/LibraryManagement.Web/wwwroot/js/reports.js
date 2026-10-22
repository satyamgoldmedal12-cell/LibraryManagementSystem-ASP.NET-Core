(function ($) {
    'use strict';
    $('#Report').on('change',function () {
        var statuses=$(this).find('option:selected').attr('data-statuses').split(','),select=$('#Status').empty();
        statuses.forEach(function(status){$('<option>').val(status).text(status.replace(/-/g,' ').replace(/^./,function(c){return c.toUpperCase();})).appendTo(select);});
    });
})(jQuery);
