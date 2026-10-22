(function ($) {
    'use strict';
    function memberFields() {
        var member=$('#Role').val()==='Member',section=$('#user-member-fields');
        section.toggleClass('d-none',!member);
        if(!member){section.find('.lookup-id').val('');section.find('.lookup-input').val('');section.find('.lookup-results').empty();}
    }
    $('#Role').on('change',memberFields);memberFields();
})(jQuery);
