// Dynamic middle-segment rows + cover-image preview for the package form.
(function () {
    function reindex() {
        var rows = document.querySelectorAll('#segments-container .segment-row');
        rows.forEach(function (row, i) {
            row.querySelector('.segment-index').textContent = i + 1;
            row.querySelectorAll('select, input').forEach(function (field) {
                if (field.name) {
                    field.name = field.name.replace(/Segments\[\d+\]/, 'Segments[' + i + ']');
                }
            });
        });
    }

    function addRow() {
        var tpl = document.getElementById('segment-row-template');
        var index = document.querySelectorAll('#segments-container .segment-row').length;
        var html = tpl.innerHTML.replace(/__INDEX__/g, index);
        var tbody = document.getElementById('segments-container');
        tbody.insertAdjacentHTML('beforeend', html);
        reindex();
    }

    document.addEventListener('DOMContentLoaded', function () {
        var addBtn = document.getElementById('add-segment');
        if (addBtn) addBtn.addEventListener('click', addRow);

        var container = document.getElementById('segments-container');
        if (container) {
            container.addEventListener('click', function (e) {
                var btn = e.target.closest('.remove-segment');
                if (!btn) return;
                btn.closest('.segment-row').remove();
                reindex();
            });
        }

        var fileInput = document.getElementById('coverImageInput');
        var preview = document.getElementById('coverPreview');
        if (fileInput && preview) {
            fileInput.addEventListener('change', function () {
                var file = this.files && this.files[0];
                if (!file) return;
                preview.src = URL.createObjectURL(file);
                preview.classList.remove('d-none');
            });
        }
    });
})();
