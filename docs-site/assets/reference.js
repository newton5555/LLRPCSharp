(function () {
  const searchInput = document.getElementById('api-search');
  const resultCount = document.getElementById('api-result-count');
  const typeCards = Array.from(document.querySelectorAll('.api-type'));
  const navLinks = Array.from(document.querySelectorAll('.reference-nav a[href^="#"]'));
  const quickCards = Array.from(document.querySelectorAll('.quick-jump-card'));
  const filterPills = Array.from(document.querySelectorAll('.filter-pill'));
  let currentCategory = 'all';

  function runFilter() {
    const query = searchInput ? searchInput.value.trim().toLowerCase() : '';
    let matchingTypes = 0;

    // Filter type cards
    typeCards.forEach(card => {
      const typeCategory = card.dataset.category || '';
      const categoryMatch = currentCategory === 'all' || typeCategory.includes(currentCategory);
      
      const searchData = `${card.dataset.search || ''} ${card.innerText || ''}`.toLowerCase();
      const queryMatch = !query || searchData.includes(query);

      const isVisible = categoryMatch && queryMatch;
      card.style.display = isVisible ? '' : 'none';
      if (isVisible) matchingTypes++;

      // If query is present, also filter internal rows if applicable
      if (query && isVisible) {
        const rows = card.querySelectorAll('.member-table tr.api-entry');
        rows.forEach(row => {
          const rowData = `${row.dataset.search || ''} ${row.innerText || ''}`.toLowerCase();
          const rowMatch = rowData.includes(query);
          row.style.opacity = rowMatch ? '1' : '0.4';
        });
      } else {
        const rows = card.querySelectorAll('.member-table tr.api-entry');
        rows.forEach(row => {
          row.style.opacity = '1';
        });
      }
    });

    // Filter quick jump cards
    quickCards.forEach(card => {
      const searchData = `${card.dataset.search || ''} ${card.innerText || ''}`.toLowerCase();
      const cardCat = card.dataset.category || '';
      const catMatch = currentCategory === 'all' || cardCat.includes(currentCategory);
      const qMatch = !query || searchData.includes(query);
      card.style.display = (catMatch && qMatch) ? '' : 'none';
    });

    // Filter sidebar navigation links
    navLinks.forEach(link => {
      const href = link.getAttribute('href');
      if (href && href.startsWith('#') && !href.startsWith('#info-') && href !== '#quick-jump') {
        const targetId = href.substring(1);
        const targetEl = document.getElementById(targetId);
        if (targetEl) {
          const isTargetVisible = targetEl.style.display !== 'none';
          link.style.display = isTargetVisible ? '' : 'none';
        }
      }
    });

    // Update count display
    if (resultCount) {
      if (query || currentCategory !== 'all') {
        resultCount.textContent = `${matchingTypes} symbols matching filter`;
      } else {
        resultCount.textContent = `${typeCards.length} public symbols documented`;
      }
    }
  }

  // Bind search input
  if (searchInput) {
    searchInput.addEventListener('input', runFilter);
  }

  // Bind category filter pills
  filterPills.forEach(pill => {
    pill.addEventListener('click', () => {
      filterPills.forEach(p => p.classList.remove('active'));
      pill.classList.add('active');
      currentCategory = pill.dataset.filter || 'all';
      runFilter();
    });
  });

  // Copy code blocks
  document.querySelectorAll('.copy-example').forEach(button => {
    button.addEventListener('click', async () => {
      const pre = button.closest('.api-example')?.querySelector('code') || button.parentElement.nextElementSibling?.querySelector('code');
      if (!pre) return;
      try {
        await navigator.clipboard.writeText(pre.innerText.trim());
        const originalText = button.textContent;
        button.textContent = 'Copied!';
        setTimeout(() => { button.textContent = originalText; }, 1500);
      } catch (err) {
        button.textContent = 'Failed';
      }
    });
  });

  // Scrollspy for active navigation link
  const sections = Array.from(document.querySelectorAll('.reference-section, .api-type'));
  function updateScrollspy() {
    const scrollPos = window.scrollY + 100;
    let currentId = '';
    sections.forEach(section => {
      if (section.style.display !== 'none' && section.offsetTop <= scrollPos) {
        currentId = section.id;
      }
    });
    if (currentId) {
      navLinks.forEach(link => {
        link.classList.toggle('active', link.getAttribute('href') === `#${currentId}`);
      });
    }
  }

  window.addEventListener('scroll', updateScrollspy, { passive: true });
  updateScrollspy();
  runFilter();
})();
