(function ($) {
    'use strict';
    $('[data-login-portal]').on('change', function () {
        if (this.value) { window.location.assign(this.value); }
    });
    $('form[data-confirm]').on('submit', function (event) {
        if (!window.confirm($(this).attr('data-confirm'))) { event.preventDefault(); }
    });
    $('.lookup').each(function () {
        var root = $(this), timer, request, generation = 0;
        root.find('.lookup-input').on('input', function () {
            var text = $(this).val().trim(), version = ++generation;
            clearTimeout(timer);
            if (request) { request.abort(); }
            root.find('.lookup-id').val(root.find('.lookup-id').attr('data-optional') === 'true' ? '' : '0').trigger('change');
            root.find('.lookup-selection').text('Choose a result below.');
            root.find('.lookup-results').empty();
            if (text.length < 2) { return; }
            timer = setTimeout(function () {
                request = $.ajax({ url: root.attr('data-url'), data: { search: text }, dataType: 'json' })
                    .done(function (items) {
                        if (version !== generation) { return; }
                        var list = root.find('.lookup-results').empty();
                        if (!items.length) { list.append($('<div class="list-group-item">').text('No matches found.')); }
                        items.forEach(function (item) {
                            $('<button type="button" class="list-group-item list-group-item-action">').text(item.text).prop('disabled', item.disabled === true)
                                .on('click', function () {
                                    root.find('.lookup-id').val(item.id).trigger('change');
                                    root.find('.lookup-input').val(item.text);
                                    root.find('.lookup-selection').text('Selected: ' + item.text);
                                    list.empty();
                                }).appendTo(list);
                        });
                    }).fail(function (_, status) {
                        if (status !== 'abort' && version === generation) {
                            root.find('.lookup-results').empty().append($('<div class="list-group-item text-danger">').text('Search failed. Refresh your session and try again.'));
                        }
                    });
            }, 300);
        });
    });
})(jQuery);
