// Common scripts for /webapp (home/chat)
window.WebApp = window.WebApp || {};
WebApp.setTheme = (theme)=>{
  document.documentElement.setAttribute('data-theme', theme);
  localStorage.setItem('webapp-theme', theme);
};
WebApp.toggleTheme = ()=>{
  const cur = document.documentElement.getAttribute('data-theme')==='dark'?'light':'dark';
  WebApp.setTheme(cur);
};

