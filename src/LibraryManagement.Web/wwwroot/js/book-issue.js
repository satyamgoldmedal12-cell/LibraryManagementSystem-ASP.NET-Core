(function ($) {
    'use strict';
    var form = $('#issue-form'), panel = $('#issue-availability'), due = $('#DueDate');
    if (!form.length) return;
    var request, generation = 0, ready = false;
    var confirm = $('#confirm-issue'), message = $('#issue-check-message');
    function check() {
        var version = ++generation;
        if (request) request.abort();
        ready = false; confirm.prop('disabled', true); panel.attr('aria-busy', 'true');
        message.text('Checking current availability…');
        var data = { bookId: $('#BookId').val(), memberId: $('#MemberId').val() };
        data.dueDate = due.val();
        request = $.ajax({url: form.attr('data-availability-url'), data: data, dataType: 'json'})
            .done(function (result) {
                if (version !== generation) return;
                due.attr({min: result.minimumDueDate, max: result.maximumDueDate});
                $('#issue-stock').text(result.bookFound ? result.availableCopies + ' / ' + result.totalCopies + ' copies available' : 'Select a book');
                $('#issue-member-count').text(result.memberFound ? result.activeLoans + ' / ' + result.maxBooks + ' active issues' : 'Select a member');
                $('#issue-member-expiry').text(result.expiresOn || '—');
                var problems = $('#issue-problems').empty();
                result.messages.forEach(function (problem) { $('<li>').text(problem).appendTo(problems); });
                ready = result.canIssue && !!due.val();
                confirm.prop('disabled', !ready);
                message.text(ready ? 'Ready to issue. Availability is checked again on confirmation.' : 'Resolve the items above before checkout.');
            }).fail(function (_, status) {
                if (version !== generation || status === 'abort') return;
                message.text('Availability check failed. Check your connection and select Check availability to try again.');
            }).always(function () { if (version === generation) panel.attr('aria-busy', 'false'); });
    }
    form.find('.lookup-id').on('change', check);
    due.on('input change', check);
    $('#refresh-availability').on('click', check);
    form.on('submit', function (event) {
        if (!ready || !form[0].checkValidity() || (form.valid && !form.valid())) { event.preventDefault(); return; }
        confirm.prop('disabled', true).text('Recording checkout…');
    });
    check();
})(jQuery);
