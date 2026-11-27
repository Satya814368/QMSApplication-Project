(() => {
 const team=document.getElementById('risk-team'), owner=document.getElementById('risk-owner'), deliverable=document.getElementById('risk-deliverable'), search=document.getElementById('risk-deliverable-search');
 function filter(select, term='') { if(!select || !team)return; const current=select.value; for(const o of select.options){const allowed=!o.value || (o.dataset.team===team.value && o.textContent.toLowerCase().includes(term.toLowerCase())); o.hidden=!allowed; o.disabled=!allowed;} if(!Array.from(select.options).some(o=>o.value===current&&!o.disabled))select.value=''; }
 function refresh(){filter(owner);filter(deliverable,search?.value||'');}
 team?.addEventListener('change',()=>{if(search)search.value='';refresh();});search?.addEventListener('input',refresh);refresh();
 const kind=document.getElementById('risk-kind'), p=document.getElementById('risk-probability'), i=document.getElementById('risk-impact'), output=document.getElementById('risk-score');
 function score(){if(!output||!p||!i)return;const risk=kind?.value==='Risk';p.disabled=i.disabled=!risk;const n=Number(p.value)*Number(i.value);output.textContent=risk?`Risk index: ${n} / 25 — ${n>=15?'Critical':n>=10?'High':n>=5?'Medium':'Low'}`:'';output.className=n>=15?'text-danger':n>=10?'text-warning':n>=5?'text-info':'text-success';}
 [kind,p,i].forEach(x=>x?.addEventListener('input',score));score();
})();
