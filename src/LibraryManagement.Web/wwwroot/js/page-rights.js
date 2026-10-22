(() => {
    'use strict';
    const editor = document.querySelector('#module-path-editor');
    if (editor) {
        const leaf = editor.querySelector('#module-leaf');
        const name = editor.querySelector('#module-name');
        const parent = editor.querySelector('#module-parent');
        const controller = editor.querySelector('#module-controller');
        const isLeaf = () => leaf.type === 'hidden' ? leaf.value.toLowerCase() === 'true' : leaf.checked;
        const update = () => {
            const page = isLeaf();
            editor.querySelectorAll('[data-leaf-field]').forEach(el => { el.hidden = !page; });
            controller.disabled = !page;
            if (!page) editor.querySelectorAll('input[type="checkbox"][name^="Is"]').forEach(c => { if(c !== leaf && c.name !== 'IsActive') c.checked = false; });
            const option = parent.selectedOptions[0];
            document.querySelector('#module-full-path').value = [option?.dataset.fullPath, name.value].filter(Boolean).join(' / ');
            document.querySelector('#module-route').value = page ? '/' + controller.value + '/Index' : '#';
        };
        editor.addEventListener('input', update); editor.addEventListener('change', update); update();
        editor.addEventListener('change', event => {
            if(!event.target.matches('input[type="checkbox"][name^="Is"]') || event.target === leaf || event.target.name === 'IsActive') return;
            const read = editor.querySelector('input[type="checkbox"][name="IsRead"]');
            if(event.target === read && !read.checked) editor.querySelectorAll('[data-leaf-field] input[type="checkbox"]').forEach(c => { c.checked = false; });
            else if(event.target.checked && isLeaf()) read.checked = true;
        });
    }
    const selector = document.querySelector('#page-rights-selector');
    if (selector) {
        const kind = selector.querySelector('#rights-kind'), user = selector.querySelector('#rights-user'), role = selector.querySelector('#rights-role');
        const category = selector.querySelector('#rights-category'), search = selector.querySelector('#rights-search'), status = selector.querySelector('#rights-search-status');
        const hideLoaded = () => document.querySelectorAll('[data-loaded-rights]').forEach(el => { el.hidden = true; });
        const mode = () => {
            const byUser = kind.value === 'user';
            selector.querySelectorAll('[data-user-field]').forEach(el => { el.hidden = !byUser; });
            selector.querySelectorAll('[data-role-field]').forEach(el => { el.hidden = byUser; });
            user.disabled = !byUser; role.disabled = byUser;
        };
        let timer, pending, generation=0;
        kind.addEventListener('change', () => { mode(); hideLoaded(); });
        category.addEventListener('change', () => { user.replaceChildren(new Option('Select a user', '')); search.value=''; hideLoaded(); ++generation; pending?.abort(); });
        user.addEventListener('change', hideLoaded); role.addEventListener('change', hideLoaded); mode();
        search.addEventListener('input', () => {
            clearTimeout(timer); pending?.abort(); const current=++generation, text=search.value.trim();
            if(text.length < 2) { status.textContent='Type at least 2 letters.'; return; }
            timer=setTimeout(async () => {
                pending=new AbortController();status.textContent='Searching users…';
                try {
                    const url=new URL(selector.dataset.targetsUrl,location.origin);url.searchParams.set('search',text);url.searchParams.set('category',category.value);
                    const response=await fetch(url,{signal:pending.signal,headers:{Accept:'application/json'}});
                    if(!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Lookup failed');
                    const rows=await response.json(); if(current!==generation)return;
                    user.replaceChildren(new Option('Select a user','')); rows.forEach(row => user.add(new Option(row.text,row.id)));
                    status.textContent=rows.length ? 'Choose a user and load page rights.' : 'No matching users. Refine the search.';
                } catch(error) { if(error.name !== 'AbortError') status.textContent='Could not load users. Refresh the page and try again.'; }
            },250);
        });
    }
    const tree = document.querySelector('.rights-tree');
    if (tree) {
        const rows=Array.from(tree.querySelectorAll('tbody tr')), index=new Map(rows.map(row => [row.dataset.moduleId,row]));
        const collapsed=new Set(), search=document.querySelector('#page-tree-search');
        const ancestors=row => {const result=[],seen=new Set();let id=row.dataset.parentId;while(id && index.has(id) && !seen.has(id)){seen.add(id);result.push(id);id=index.get(id).dataset.parentId;}return result;};
        const update=() => {
            const term=search.value.trim().toLowerCase(), visible=new Set();
            if(term) rows.filter(row => row.dataset.moduleSearch.toLowerCase().includes(term)).forEach(row => {visible.add(row.dataset.moduleId);ancestors(row).forEach(id=>visible.add(id));});
            rows.forEach(row => {row.hidden=row.dataset.deleted==='true'||(term?!visible.has(row.dataset.moduleId):ancestors(row).some(id=>collapsed.has(id)));});
            tree.querySelectorAll('[data-tree-toggle]').forEach(button => {const closed=collapsed.has(button.dataset.treeToggle);button.textContent=closed?'▸':'▾';button.setAttribute('aria-expanded',String(!closed));});
        };
        tree.addEventListener('click',event=>{const button=event.target.closest('[data-tree-toggle]');if(!button)return;const id=button.dataset.treeToggle;if(collapsed.has(id))collapsed.delete(id);else collapsed.add(id);update();});
        document.querySelector('[data-tree-expand]').addEventListener('click',()=>{collapsed.clear();search.value='';update();});
        document.querySelector('[data-tree-collapse]').addEventListener('click',()=>{tree.querySelectorAll('[data-tree-toggle]').forEach(b=>collapsed.add(b.dataset.treeToggle));search.value='';update();});
        search.addEventListener('input',update);
        tree.addEventListener('change',event=>{
            const box=event.target;if(!box.matches('input[type="checkbox"][data-right]'))return;
            const row=box.closest('tr'),read=row.querySelector('[data-right="Read"]');
            if(box===read&&!read.checked)row.querySelectorAll('input[type="checkbox"]').forEach(c=>{c.checked=false;});
            else if(box.checked&&!read.disabled)read.checked=true;
        });
    }
})();
