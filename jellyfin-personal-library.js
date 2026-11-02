/*!
 * Jellyfin Personal Library — v0.1.0
 * https://github.com/k1k4k/jellyfin-personal-library
 *
 * Chaque utilisateur choisit ce qu'il voit, type de contenu par type de contenu (musique, films,
 * séries…) : tout, tout sauf ce qu'il masque, ou uniquement ce qu'il garde.
 *
 * Each user chooses what they see, content type by content type (music, movies, shows…):
 * everything, everything except what they hide, or only what they keep.
 *
 * Cible / target : jellyfin-web 10.11+ (développé sur 12.1). Le plugin serveur fournit l'API
 * /PersonalLibrary et fait le masquage (étiquettes du profil) : ce script n'est que l'interface.
 * Aucune modification du bundle : les modules internes (actionSheet, playbackManager) sont
 * retrouvés via le runtime webpack, par leur contenu.
 *
 * Licence MIT.
 */
(function () {
    'use strict';

    if (window.__jfPersonalLibrary) return;
    var VERSION = '0.1.1';
    var TAG = '[PersonalLibrary]';
    window.__jfPersonalLibrary = { version: VERSION };

    /* ------------------------------------------------------------------ */
    /*  0. Textes FR / EN                                                  */
    /* ------------------------------------------------------------------ */

    var STRINGS = {
        fr: {
            title: 'Ma bibliothèque', close: 'Fermer', loading: 'Chargement…', error: 'Erreur',
            modeAll: 'Tout', modeAllDesc: 'Tu vois tout ce que le serveur propose.',
            modeExcept: 'Tout sauf…', modeExceptDesc: 'Tu masques ce que tu ne veux pas voir.',
            modeOnly: 'Uniquement…', modeOnlyDesc: 'Tu ne vois que ce que tu choisis.',
            onlyRefused: 'Indisponible : ton profil est déjà restreint par l\'administrateur.',
            readOnly: 'L\'administrateur a désactivé la modification de ta bibliothèque.',
            hiddenList: 'Masqués', keptList: 'Gardés',
            onlyEmpty: 'Rien de choisi : ce type de contenu est entièrement caché.',
            exceptEmpty: 'Rien de masqué pour l\'instant.',
            search: 'Rechercher…', more: 'Plus…', nothingHere: 'Rien ici.', noResult: 'Aucun résultat.',
            hide: 'Masquer', unhide: 'Afficher', keep: 'Garder', unkeep: 'Retirer', included: 'inclus',
            confirmTitle: 'Changer de mode ?', confirmClear: 'Tes choix actuels pour ce type seront effacés.',
            confirmAll: 'Tout afficher de nouveau ? Tes choix pour ce type seront effacés.',
            yes: 'Continuer', no: 'Annuler',
            upToDate: 'À jour', updating: 'Mise à jour…',
            note: 'Les listes de Jellyfin se mettent à jour quand tu changes de page.',
            menuHide: 'Masquer de ma bibliothèque', menuUnkeep: 'Retirer de ma bibliothèque', menuOpen: 'Ma bibliothèque…',
            toastHidden: '« {name} » est masqué de ta bibliothèque.', toastRemoved: '« {name} » est retiré de ta bibliothèque.',
            libraries: 'Bibliothèques'
        },
        en: {
            title: 'My library', close: 'Close', loading: 'Loading…', error: 'Error',
            modeAll: 'Everything', modeAllDesc: 'You see everything the server offers.',
            modeExcept: 'All except…', modeExceptDesc: 'You hide what you don\'t want to see.',
            modeOnly: 'Only…', modeOnlyDesc: 'You only see what you choose.',
            onlyRefused: 'Unavailable: your profile is already restricted by the administrator.',
            readOnly: 'The administrator has disabled changes to your library.',
            hiddenList: 'Hidden', keptList: 'Kept',
            onlyEmpty: 'Nothing chosen: this content type is completely hidden.',
            exceptEmpty: 'Nothing hidden yet.',
            search: 'Search…', more: 'More…', nothingHere: 'Nothing here.', noResult: 'No results.',
            hide: 'Hide', unhide: 'Show', keep: 'Keep', unkeep: 'Remove', included: 'included',
            confirmTitle: 'Change mode?', confirmClear: 'Your current choices for this type will be cleared.',
            confirmAll: 'Show everything again? Your choices for this type will be cleared.',
            yes: 'Continue', no: 'Cancel',
            upToDate: 'Up to date', updating: 'Updating…',
            note: 'Jellyfin lists refresh when you change page.',
            menuHide: 'Hide from my library', menuUnkeep: 'Remove from my library', menuOpen: 'My library…',
            toastHidden: '"{name}" is hidden from your library.', toastRemoved: '"{name}" is removed from your library.',
            libraries: 'Libraries'
        }
    };

    var TYPE_NAMES = {
        fr: { music: 'Musique', movies: 'Films', tvshows: 'Séries', boxsets: 'Collections', musicvideos: 'Clips', homevideos: 'Vidéos perso', books: 'Livres', photos: 'Photos', mixed: 'Mixte' },
        en: { music: 'Music', movies: 'Movies', tvshows: 'Shows', boxsets: 'Collections', musicvideos: 'Music videos', homevideos: 'Home videos', books: 'Books', photos: 'Photos', mixed: 'Mixed' }
    };

    var ITEM_NAMES = {
        fr: { MusicArtist: 'Artiste', MusicAlbum: 'Album', Audio: 'Titre', Movie: 'Film', Series: 'Série', Season: 'Saison', Episode: 'Épisode', BoxSet: 'Collection', Folder: 'Dossier', CollectionFolder: 'Bibliothèque', MusicVideo: 'Clip', Video: 'Vidéo', Book: 'Livre', AudioBook: 'Livre audio', Photo: 'Photo', PhotoAlbum: 'Album photo' },
        en: { MusicArtist: 'Artist', MusicAlbum: 'Album', Audio: 'Track', Movie: 'Movie', Series: 'Show', Season: 'Season', Episode: 'Episode', BoxSet: 'Collection', Folder: 'Folder', CollectionFolder: 'Library', MusicVideo: 'Music video', Video: 'Video', Book: 'Book', AudioBook: 'Audiobook', Photo: 'Photo', PhotoAlbum: 'Photo album' }
    };

    // Ce que la recherche de la fenêtre cherche, par type de contenu (vide : tout).
    var SEARCH_TYPES = { music: 'MusicArtist,MusicAlbum', movies: 'Movie,BoxSet', tvshows: 'Series', boxsets: 'BoxSet', musicvideos: 'MusicVideo', books: 'Book,AudioBook' };

    function getLang() {
        var l = (document.documentElement.lang || navigator.language || 'en').toLowerCase();
        return l.indexOf('fr') === 0 ? 'fr' : 'en';
    }

    function t(key, args) {
        var lang = getLang();
        var s = (STRINGS[lang] && STRINGS[lang][key]) || STRINGS.en[key] || key;
        if (args) {
            Object.keys(args).forEach(function (k) { s = s.split('{' + k + '}').join(args[k]); });
        }
        return s;
    }

    function typeName(type) {
        return TYPE_NAMES[getLang()][type] || type;
    }

    function itemName(kind) {
        return ITEM_NAMES[getLang()][kind] || kind;
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function sameId(a, b) {
        return String(a || '').replace(/-/g, '').toLowerCase() === String(b || '').replace(/-/g, '').toLowerCase();
    }

    /* ------------------------------------------------------------------ */
    /*  1. Modules internes via le runtime webpack                         */
    /* ------------------------------------------------------------------ */

    var pm = null;             // playbackManager
    var actionSheetObj = null; // export par défaut de components/actionSheet/actionSheet

    function getWebpackRequire() {
        return new Promise(function (resolve) {
            var chunks = (self.webpackChunk = self.webpackChunk || []);
            chunks.push([['jfPersonalLibrary_' + Date.now()], {}, function (req) { resolve(req); }]);
        });
    }

    var scanned = {};

    function scanModules(req) {
        var modules = req.m || {};
        var ids = Object.keys(modules);
        for (var i = 0; i < ids.length; i++) {
            if (pm && actionSheetObj) return;
            var id = ids[i];
            if (scanned[id]) continue;
            scanned[id] = true;
            var src;
            try { src = Function.prototype.toString.call(modules[id]); } catch (e) { continue; }
            var maybePm = !pm && /setCurrentPlaylistItem\s*[=(]/.test(src) && /getCurrentPlaylistItemId\s*[=(]/.test(src) && src.indexOf('_currentPlayer') !== -1;
            var maybeSheet = !actionSheetObj && src.indexOf('actionSheetMenuItem') !== -1 && src.indexOf('actionSheetScroller') !== -1;
            if (!maybePm && !maybeSheet) continue;
            var exp;
            // Dépendances dans un chunk pas encore arrivé : on réessaiera au prochain chunk.
            try { exp = req(id); } catch (e) { delete scanned[id]; continue; }
            if (!exp) continue;
            var keys = Object.keys(exp);
            for (var k = 0; k < keys.length; k++) {
                var v = exp[keys[k]];
                if (!v) continue;
                if (maybePm && typeof v.canPlay === 'function' && typeof v.getCurrentPlayer === 'function' && typeof v.setCurrentPlaylistItem === 'function') pm = v;
                if (maybeSheet && typeof v.show === 'function' && keys[k] === 'default') actionSheetObj = v;
            }
        }
    }

    /* ------------------------------------------------------------------ */
    /*  2. API du plugin                                                   */
    /* ------------------------------------------------------------------ */

    var me = null; // GET /PersonalLibrary/Me
    var meUser = null;

    function api() {
        return window.ApiClient || null;
    }

    function currentUserId() {
        var c = api();
        try { return c && c.getCurrentUserId ? c.getCurrentUserId() : null; } catch (e) { return null; }
    }

    function apiJson(method, path, body, query) {
        var c = api();
        var opts = { type: method, url: c.getUrl(path, query), dataType: 'json' };
        if (body !== undefined) {
            opts.data = JSON.stringify(body);
            opts.contentType = 'application/json';
        }
        return c.fetch(opts);
    }

    function apiSend(method, path, body) {
        var c = api();
        var opts = { type: method, url: c.getUrl(path) };
        if (body !== undefined) {
            opts.data = JSON.stringify(body);
            opts.contentType = 'application/json';
        }
        return c.fetch(opts);
    }

    // Ordre des onglets : les types courants d'abord, Collections et Mixte à la fin.
    var TYPE_ORDER = ['music', 'movies', 'tvshows', 'musicvideos', 'homevideos', 'books', 'photos', 'mixed', 'boxsets'];

    function sortTypes(m) {
        var rank = function (x) { var i = TYPE_ORDER.indexOf(x.type); return i === -1 ? TYPE_ORDER.length - 2 : i; };
        m.types.sort(function (a, b) { return rank(a) - rank(b); });
        return m;
    }

    function loadMe(force) {
        var uid = currentUserId();
        if (!uid) return Promise.resolve(null);
        if (me && meUser === uid && !force) return Promise.resolve(me);
        return apiJson('GET', 'PersonalLibrary/Me').then(function (m) {
            me = sortTypes(m);
            meUser = uid;
            return m;
        }, function () { return null; });
    }

    function typeOf(type) {
        return me && me.types.filter(function (x) { return x.type === type; })[0];
    }

    function setMode(type, mode) {
        return apiSend('PUT', 'PersonalLibrary/Me/Types/' + encodeURIComponent(type), { Mode: mode });
    }

    function addRule(type, itemId) {
        return apiSend('POST', 'PersonalLibrary/Me/Types/' + encodeURIComponent(type) + '/Rules', { ItemId: itemId });
    }

    function removeRule(type, itemId) {
        return apiSend('DELETE', 'PersonalLibrary/Me/Types/' + encodeURIComponent(type) + '/Rules/' + itemId);
    }

    function clearRules(type) {
        var x = typeOf(type);
        return (x ? x.rules : []).reduce(function (p, r) {
            return p.then(function () { return removeRule(type, r.ItemId); });
        }, Promise.resolve());
    }

    /* ------------------------------------------------------------------ */
    /*  3. Styles et briques d'interface                                   */
    /* ------------------------------------------------------------------ */

    function injectStyles() {
        if (document.getElementById('jfplStyles')) return;
        var css = [
            '.jfpl-overlay{position:fixed;inset:0;z-index:2000;background:rgba(0,0,0,.6);display:flex;align-items:center;justify-content:center;padding:16px;box-sizing:border-box}',
            '.jfpl-dialog{background:#202020;color:#eee;border-radius:10px;box-shadow:0 10px 40px rgba(0,0,0,.6);width:min(760px,100%);height:min(820px,calc(100vh - 32px));display:flex;flex-direction:column;overflow:hidden;font-size:15px}',
            '.jfpl-dialog.jfpl-small{width:min(440px,100%);height:auto}',
            '.jfpl-head,.jfpl-tabs,.jfpl-foot{flex:none}',
            '.jfpl-head{display:flex;align-items:center;gap:8px;padding:14px 16px;border-bottom:1px solid rgba(255,255,255,.08)}',
            '.jfpl-head h2{margin:0;font-size:1.15em;font-weight:600;flex:1;min-width:0}',
            '.jfpl-x{background:none;border:0;color:inherit;cursor:pointer;padding:4px;border-radius:50%;line-height:0}',
            '.jfpl-x:hover{background:rgba(255,255,255,.1)}',
            '.jfpl-tabs{display:flex;gap:4px;padding:8px 12px 0;overflow-x:auto;border-bottom:1px solid rgba(255,255,255,.08)}',
            '.jfpl-tab{background:none;border:0;border-bottom:2px solid transparent;color:inherit;opacity:.7;padding:8px 12px;font:inherit;cursor:pointer;white-space:nowrap}',
            '.jfpl-tab.jfpl-sel{opacity:1;border-bottom-color:#00a4dc}',
            '.jfpl-tab .jfpl-dot{display:inline-block;width:7px;height:7px;border-radius:50%;background:#00a4dc;margin-left:6px;vertical-align:middle}',
            '.jfpl-body{overflow-x:hidden;overflow-y:auto;padding:12px 16px;flex:1 1 auto;min-height:0}',
            '.jfpl-foot{display:flex;flex-wrap:wrap;align-items:center;gap:8px;padding:10px 16px;border-top:1px solid rgba(255,255,255,.08);font-size:.9em}',
            '.jfpl-foot .jfpl-sum{flex:1;min-width:0;opacity:.75}',
            '.jfpl-btn{background:rgba(255,255,255,.1);color:inherit;border:0;border-radius:6px;padding:7px 14px;font:inherit;cursor:pointer;white-space:nowrap}',
            '.jfpl-btn:hover{background:rgba(255,255,255,.18)}',
            '.jfpl-btn:disabled{opacity:.4;cursor:default}',
            '.jfpl-btn.jfpl-primary{background:#00a4dc;color:#fff}',
            '.jfpl-btn.jfpl-on{background:rgba(0,164,220,.18);color:#5ccdf5}',
            '.jfpl-modes{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:8px;margin-bottom:12px}',
            '.jfpl-mode{border:1px solid rgba(255,255,255,.15);border-radius:8px;padding:10px 12px;cursor:pointer;background:none;color:inherit;font:inherit;text-align:left}',
            '.jfpl-mode b{display:block;margin-bottom:4px}',
            '.jfpl-mode small{display:block;opacity:.7;line-height:1.35}',
            '.jfpl-mode.jfpl-sel{border-color:#00a4dc;background:rgba(0,164,220,.12)}',
            '.jfpl-mode:disabled{opacity:.45;cursor:default}',
            '.jfpl-notice{background:rgba(240,180,41,.12);color:#f0c75e;border-radius:6px;padding:8px 10px;margin:0 0 12px}',
            '.jfpl-section{margin:14px 0 6px;font-weight:600}',
            '.jfpl-chips{display:flex;flex-wrap:wrap;gap:6px}',
            '.jfpl-chip{display:inline-flex;align-items:center;gap:4px;background:rgba(255,255,255,.08);border-radius:14px;padding:3px 4px 3px 10px;max-width:100%}',
            '.jfpl-chip span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
            '.jfpl-chip.jfpl-gone{opacity:.5;text-decoration:line-through}',
            '.jfpl-chip button{background:none;border:0;color:inherit;cursor:pointer;line-height:0;padding:2px;border-radius:50%;opacity:.7}',
            '.jfpl-chip button:hover{background:rgba(255,255,255,.15);opacity:1}',
            '.jfpl-chip .material-icons{font-size:16px}',
            '.jfpl-empty{opacity:.65;font-style:italic}',
            '.jfpl-search{width:100%;box-sizing:border-box;background:rgba(255,255,255,.07);border:1px solid rgba(255,255,255,.12);border-radius:6px;color:inherit;font:inherit;padding:8px 10px;margin:4px 0 8px}',
            '.jfpl-crumbs{display:flex;flex-wrap:wrap;align-items:center;gap:2px;margin-bottom:6px;font-size:.92em}',
            '.jfpl-crumbs button{background:none;border:0;color:#00a4dc;cursor:pointer;font:inherit;padding:2px 4px}',
            '.jfpl-crumbs span{opacity:.5}',
            '.jfpl-row{display:flex;align-items:center;gap:8px;padding:6px 4px;border-radius:6px}',
            '.jfpl-row:hover{background:rgba(255,255,255,.05)}',
            '.jfpl-row .jfpl-name{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
            '.jfpl-row .jfpl-name.jfpl-link{cursor:pointer}',
            '.jfpl-row .jfpl-name.jfpl-link:hover{text-decoration:underline}',
            '.jfpl-row .jfpl-meta{opacity:.55;font-size:.88em;white-space:nowrap}',
            '.jfpl-row .jfpl-inc{opacity:.6;font-size:.88em;font-style:italic;padding:0 6px}',
            '.jfpl-row.jfpl-chosen .jfpl-name{color:#5ccdf5}',
            '.jfpl-msg{padding:16px 4px;opacity:.8}',
            '.jfpl-spin{display:inline-block;width:14px;height:14px;border:2px solid rgba(255,255,255,.25);border-top-color:#00a4dc;border-radius:50%;animation:jfplspin 1s linear infinite;vertical-align:middle;margin-right:6px}',
            '@keyframes jfplspin{to{transform:rotate(360deg)}}',
            '.jfpl-toast{position:fixed;left:50%;bottom:24px;transform:translateX(-50%);z-index:2100;background:#303030;color:#eee;padding:10px 16px;border-radius:8px;box-shadow:0 6px 24px rgba(0,0,0,.5);max-width:calc(100vw - 32px)}',
            '.jfplHeaderButton .material-icons{font-size:24px}',
            '.jfpl-float{position:fixed;right:16px;bottom:16px;z-index:1500;width:48px;height:48px;border-radius:50%;border:0;background:#00a4dc;color:#fff;box-shadow:0 4px 14px rgba(0,0,0,.5);cursor:pointer}',
            '@media (max-width:600px){.jfpl-overlay{padding:0}.jfpl-dialog{height:100vh;border-radius:0}}'
        ].join('\n');
        var style = document.createElement('style');
        style.id = 'jfplStyles';
        style.textContent = css;
        document.head.appendChild(style);
    }

    function icon(name) {
        return '<span class="material-icons ' + name + '" aria-hidden="true"></span>';
    }

    function overlay(dialogClass) {
        injectStyles();
        var ov = document.createElement('div');
        ov.className = 'jfpl-overlay';
        ov.innerHTML = '<div class="jfpl-dialog ' + (dialogClass || '') + '" role="dialog" aria-modal="true"></div>';
        document.body.appendChild(ov);
        var dlg = ov.firstChild;
        var closed = false;
        var onKey = function (e) { if (e.key === 'Escape') { e.stopPropagation(); close(); } };
        function close() {
            if (closed) return;
            closed = true;
            document.removeEventListener('keydown', onKey, true);
            ov.remove();
        }
        document.addEventListener('keydown', onKey, true);
        ov.addEventListener('mousedown', function (e) { if (e.target === ov) close(); });
        return { overlay: ov, dialog: dlg, close: close, isClosed: function () { return closed; } };
    }

    function confirmBox(text) {
        return new Promise(function (resolve) {
            var ui = overlay('jfpl-small');
            ui.overlay.style.zIndex = 2050;
            ui.dialog.innerHTML = '<div class="jfpl-head"><h2>' + escapeHtml(t('confirmTitle')) + '</h2></div>'
                + '<div class="jfpl-body"><p>' + escapeHtml(text) + '</p></div>'
                + '<div class="jfpl-foot"><span class="jfpl-sum"></span><button class="jfpl-btn" data-no>' + escapeHtml(t('no')) + '</button>'
                + '<button class="jfpl-btn jfpl-primary" data-yes>' + escapeHtml(t('yes')) + '</button></div>';
            ui.dialog.querySelector('[data-no]').addEventListener('click', function () { ui.close(); resolve(false); });
            ui.dialog.querySelector('[data-yes]').addEventListener('click', function () { ui.close(); resolve(true); });
        });
    }

    var toastTimer = null;

    function toast(text) {
        injectStyles();
        var el = document.querySelector('.jfpl-toast');
        if (!el) {
            el = document.createElement('div');
            el.className = 'jfpl-toast';
            document.body.appendChild(el);
        }
        el.textContent = text;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { el.remove(); }, 4500);
    }

    /* ------------------------------------------------------------------ */
    /*  4. Fenêtre « Ma bibliothèque »                                     */
    /* ------------------------------------------------------------------ */

    var PAGE = 100;

    function openPage(startType) {
        loadMe(true).then(function (m) {
            if (!m) return;
            var ui = overlay();
            var view = {
                ui: ui,
                type: startType && typeOf(startType) ? startType : (m.types[0] && m.types[0].type),
                path: [],      // dossiers ouverts : [{ id, name, libraryId, chosen }]
                search: '',
                items: [],
                total: 0,
                busy: false
            };
            ui.dialog.innerHTML = '<div class="jfpl-head"><h2>' + escapeHtml(t('title')) + '</h2>'
                + '<button class="jfpl-x" title="' + escapeHtml(t('close')) + '">' + icon('close') + '</button></div>'
                + '<div class="jfpl-tabs"></div><div class="jfpl-body"></div>'
                + '<div class="jfpl-foot"><span class="jfpl-sum"></span><button class="jfpl-btn" data-close>' + escapeHtml(t('close')) + '</button></div>';
            ui.dialog.querySelector('.jfpl-x').addEventListener('click', ui.close);
            ui.dialog.querySelector('[data-close]').addEventListener('click', ui.close);
            renderTabs(view);
            renderType(view);
            if (me.pending) watchStatus(view);
            else renderStatus(view);
        });
    }

    function renderTabs(view) {
        var tabs = view.ui.dialog.querySelector('.jfpl-tabs');
        tabs.innerHTML = me.types.map(function (x) {
            return '<button class="jfpl-tab' + (x.type === view.type ? ' jfpl-sel' : '') + '" data-type="' + escapeHtml(x.type) + '">'
                + escapeHtml(typeName(x.type)) + (x.mode !== 'All' ? '<span class="jfpl-dot"></span>' : '') + '</button>';
        }).join('');
        tabs.querySelectorAll('[data-type]').forEach(function (b) {
            b.addEventListener('click', function () {
                view.type = b.getAttribute('data-type');
                view.path = [];
                view.search = '';
                renderTabs(view);
                renderType(view);
            });
        });
    }

    function renderType(view, reuse) {
        var x = typeOf(view.type);
        var body = view.ui.dialog.querySelector('.jfpl-body');
        if (!x) {
            body.innerHTML = '<div class="jfpl-msg">' + escapeHtml(t('nothingHere')) + '</div>';
            return;
        }
        var edit = me.canEdit;
        var html = '';
        if (!edit) html += '<p class="jfpl-notice">' + escapeHtml(t('readOnly')) + '</p>';
        html += '<div class="jfpl-modes">' + ['All', 'Except', 'Only'].map(function (mode) {
            var refused = mode === 'Only' && !me.onlyAllowed;
            return '<button class="jfpl-mode' + (x.mode === mode ? ' jfpl-sel' : '') + '" data-mode="' + mode + '"'
                + (!edit || refused ? ' disabled' : '') + '><b>' + escapeHtml(t('mode' + mode)) + '</b><small>'
                + escapeHtml(refused ? t('onlyRefused') : t('mode' + mode + 'Desc')) + '</small></button>';
        }).join('') + '</div>';
        if (x.mode !== 'All') {
            html += '<div class="jfpl-section">' + escapeHtml(t(x.mode === 'Only' ? 'keptList' : 'hiddenList')) + ' (' + x.rules.length + ')</div>';
            html += x.rules.length
                ? '<div class="jfpl-chips">' + x.rules.map(function (r) {
                    return '<span class="jfpl-chip' + (r.exists ? '' : ' jfpl-gone') + '" title="' + escapeHtml(itemName(r.Type)) + '"><span>' + escapeHtml(r.name) + '</span>'
                        + (edit ? '<button data-unrule="' + r.ItemId + '" title="' + escapeHtml(t(x.mode === 'Only' ? 'unkeep' : 'unhide')) + '">' + icon('close') + '</button>' : '')
                        + '</span>';
                }).join('') + '</div>'
                : '<div class="jfpl-empty">' + escapeHtml(t(x.mode === 'Only' ? 'onlyEmpty' : 'exceptEmpty')) + '</div>';
            html += '<div class="jfpl-section">' + escapeHtml(typeName(view.type)) + '</div>'
                + '<input class="jfpl-search" type="search" placeholder="' + escapeHtml(t('search')) + '" value="' + escapeHtml(view.search) + '">'
                + '<div class="jfpl-crumbs"></div><div class="jfpl-list"></div>';
        }
        body.innerHTML = html;

        body.querySelectorAll('[data-mode]').forEach(function (b) {
            b.addEventListener('click', function () { changeMode(view, b.getAttribute('data-mode')); });
        });
        body.querySelectorAll('[data-unrule]').forEach(function (b) {
            b.addEventListener('click', function () { toggle(view, b.getAttribute('data-unrule'), true); });
        });
        if (x.mode !== 'All') {
            var input = body.querySelector('.jfpl-search');
            var timer = null;
            input.addEventListener('input', function () {
                clearTimeout(timer);
                timer = setTimeout(function () {
                    view.search = input.value.trim();
                    loadList(view, false);
                }, 300);
            });
            if (reuse && view.items.length) {
                renderCrumbs(view);
                renderRows(view);
            } else {
                loadList(view, false);
            }
        }
    }

    function changeMode(view, mode) {
        var x = typeOf(view.type);
        if (!x || x.mode === mode) return;
        // Les règles n'ont de sens que dans leur mode : on repart de zéro, après confirmation.
        var ask = x.rules.length ? confirmBox(t(mode === 'All' ? 'confirmAll' : 'confirmClear')) : Promise.resolve(true);
        ask.then(function (ok) {
            if (!ok) return;
            clearRules(view.type)
                .then(function () { return setMode(view.type, mode); })
                .then(function () { return afterChange(view); })
                .catch(function (e) { console.error(TAG, 'mode', e); toast(t('error')); });
        });
    }

    function toggle(view, itemId, chosen) {
        var p = chosen ? removeRule(view.type, itemId) : addRule(view.type, itemId);
        return p.then(function () { return afterChange(view, true); })
            .catch(function (e) { console.error(TAG, 'rule', e); toast(t('error')); });
    }

    function afterChange(view, keepList) {
        return loadMe(true).then(function () {
            if (view.ui.isClosed()) return;
            var body = view.ui.dialog.querySelector('.jfpl-body');
            var scroll = body.scrollTop;
            renderTabs(view);
            renderType(view, keepList);
            body.scrollTop = scroll;
            watchStatus(view);
        });
    }

    function isChosen(view, id) {
        var x = typeOf(view.type);
        return !!x && x.rules.some(function (r) { return sameId(r.ItemId, id); });
    }

    function loadList(view, append) {
        var x = typeOf(view.type);
        var body = view.ui.dialog.querySelector('.jfpl-body');
        var listEl = body.querySelector('.jfpl-list');
        if (!listEl) return;
        renderCrumbs(view);
        if (!append) {
            view.items = [];
            view.total = 0;
            listEl.innerHTML = '<div class="jfpl-msg"><span class="jfpl-spin"></span>' + escapeHtml(t('loading')) + '</div>';
        }
        var libs = x.libraries;
        var requests;
        if (view.search) {
            requests = libs.map(function (l) {
                return apiJson('GET', 'PersonalLibrary/Me/Browse', undefined, { libraryId: l.id, searchTerm: view.search, includeItemTypes: SEARCH_TYPES[view.type] || '', limit: 60 });
            });
        } else if (!view.path.length && libs.length > 1) {
            // Plusieurs bibliothèques du même type : on commence par choisir la bibliothèque.
            view.items = libs.map(function (l) { return { id: l.id, name: l.name, type: 'CollectionFolder', isFolder: true, library: true, libraryId: l.id }; });
            view.total = view.items.length;
            renderRows(view);
            return;
        } else {
            var top = view.path[view.path.length - 1];
            var libraryId = top ? top.libraryId : libs[0].id;
            var parentId = top && !top.library ? top.id : undefined;
            requests = [apiJson('GET', 'PersonalLibrary/Me/Browse', undefined, { libraryId: libraryId, parentId: parentId, startIndex: view.items.length, limit: PAGE })
                .then(function (r) { r.items.forEach(function (i) { i.libraryId = libraryId; }); return r; })];
        }
        var token = (view.token = {});
        Promise.all(requests).then(function (results) {
            if (view.token !== token || view.ui.isClosed()) return;
            results.forEach(function (r, i) {
                r.items.forEach(function (it) { if (!it.libraryId) it.libraryId = libs[i] && libs[i].id; });
                view.items = view.items.concat(r.items);
            });
            view.total = view.search ? view.items.length : results[0].totalRecordCount;
            renderRows(view);
        }, function (e) {
            console.error(TAG, 'browse', e);
            listEl.innerHTML = '<div class="jfpl-msg">' + escapeHtml(t('error')) + '</div>';
        });
    }

    function renderCrumbs(view) {
        var el = view.ui.dialog.querySelector('.jfpl-crumbs');
        if (!el) return;
        if (view.search || !view.path.length) {
            el.innerHTML = '';
            return;
        }
        el.innerHTML = '<button data-crumb="-1">' + escapeHtml(typeName(view.type)) + '</button>'
            + view.path.map(function (p, i) {
                return '<span>›</span>' + (i === view.path.length - 1 ? '<b>' + escapeHtml(p.name) + '</b>' : '<button data-crumb="' + i + '">' + escapeHtml(p.name) + '</button>');
            }).join('');
        el.querySelectorAll('[data-crumb]').forEach(function (b) {
            b.addEventListener('click', function () {
                view.path = view.path.slice(0, parseInt(b.getAttribute('data-crumb'), 10) + 1);
                loadList(view, false);
            });
        });
    }

    // Élément couvert par un dossier choisi plus haut dans le chemin : il suit ce choix.
    function inheritedFrom(view) {
        for (var i = 0; i < view.path.length; i++) {
            if (!view.path[i].library && isChosen(view, view.path[i].id)) return view.path[i];
        }
        return null;
    }

    function renderRows(view) {
        var x = typeOf(view.type);
        var listEl = view.ui.dialog.querySelector('.jfpl-list');
        if (!listEl) return;
        if (!view.items.length) {
            listEl.innerHTML = '<div class="jfpl-msg">' + escapeHtml(t(view.search ? 'noResult' : 'nothingHere')) + '</div>';
            return;
        }
        var parent = view.search ? null : inheritedFrom(view);
        var only = x.mode === 'Only';
        listEl.innerHTML = view.items.map(function (it, idx) {
            var chosen = !it.library && isChosen(view, it.id);
            var meta = it.library ? t('libraries') : [itemName(it.type), it.albumArtist, it.productionYear].filter(Boolean).join(' · ');
            var action = '';
            if (!it.library && me.canEdit) {
                action = parent
                    ? '<span class="jfpl-inc">' + escapeHtml(t('included')) + '</span>'
                    : '<button class="jfpl-btn' + (chosen ? ' jfpl-on' : '') + '" data-toggle="' + idx + '">'
                        + escapeHtml(t(only ? (chosen ? 'unkeep' : 'keep') : (chosen ? 'unhide' : 'hide'))) + '</button>';
            }
            return '<div class="jfpl-row' + (chosen ? ' jfpl-chosen' : '') + '">'
                + '<span class="jfpl-name' + (it.isFolder ? ' jfpl-link' : '') + '" data-open="' + idx + '">' + escapeHtml(it.name) + '</span>'
                + '<span class="jfpl-meta">' + escapeHtml(meta) + '</span>' + action + '</div>';
        }).join('') + (!view.search && view.items.length < view.total ? '<div class="jfpl-msg"><button class="jfpl-btn" data-more>' + escapeHtml(t('more')) + '</button></div>' : '');

        listEl.querySelectorAll('[data-open]').forEach(function (el) {
            el.addEventListener('click', function () {
                var it = view.items[parseInt(el.getAttribute('data-open'), 10)];
                if (!it.isFolder) return;
                if (view.search) {
                    // Depuis une recherche : on ouvre le dossier à la racine de sa bibliothèque.
                    view.search = '';
                    var input = view.ui.dialog.querySelector('.jfpl-search');
                    if (input) input.value = '';
                    view.path = typeOf(view.type).libraries.length > 1
                        ? [{ id: it.libraryId, name: (typeOf(view.type).libraries.filter(function (l) { return sameId(l.id, it.libraryId); })[0] || {}).name, libraryId: it.libraryId, library: true }]
                        : [];
                }
                view.path.push({ id: it.id, name: it.name, libraryId: it.libraryId, library: !!it.library });
                loadList(view, false);
            });
        });
        listEl.querySelectorAll('[data-toggle]').forEach(function (b) {
            b.addEventListener('click', function () {
                var it = view.items[parseInt(b.getAttribute('data-toggle'), 10)];
                b.disabled = true;
                toggle(view, it.id, isChosen(view, it.id));
            });
        });
        var more = listEl.querySelector('[data-more]');
        if (more) more.addEventListener('click', function () { more.disabled = true; loadList(view, true); });
    }

    function renderStatus(view) {
        var sum = view.ui.dialog.querySelector('.jfpl-sum');
        if (!sum) return;
        sum.innerHTML = me.pending
            ? '<span class="jfpl-spin"></span>' + escapeHtml(t('updating'))
            : escapeHtml(t('upToDate')) + ' · ' + escapeHtml(t('note'));
    }

    function watchStatus(view) {
        renderStatus(view);
        if (view.watching) return;
        view.watching = true;
        var tries = 0;
        var tick = function () {
            if (view.ui.isClosed() || tries++ > 60) { view.watching = false; return; }
            apiJson('GET', 'PersonalLibrary/Me').then(function (m) {
                me = sortTypes(m);
                renderStatus(view);
                if (m.pending) setTimeout(tick, 1500);
                else view.watching = false;
            }, function () { view.watching = false; });
        };
        setTimeout(tick, 1200);
    }

    /* ------------------------------------------------------------------ */
    /*  5. Menu « ⋮ » des éléments                                         */
    /* ------------------------------------------------------------------ */

    var ITEM_MENU_IDS = ['download', 'downloadall', 'copy-stream', 'addtoplaylist', 'addtocollection', 'edit', 'editimages',
        'editsubtitles', 'refresh', 'identify', 'delete', 'share', 'moremediainfo', 'instantmix', 'shuffle', 'resume', 'play', 'queue', 'jfpd-download'];

    // Éléments passés à playbackManager.canPlay : la construction du menu d'un élément commence par là.
    var recentItems = [];

    function rememberItem(item) {
        if (!item || !item.Id || !item.Type) return;
        recentItems.push({ item: item, at: Date.now() });
        if (recentItems.length > 30) recentItems.splice(0, recentItems.length - 30);
    }

    function menuItemFor(options) {
        var now = Date.now();
        var candidates = recentItems.filter(function (r) { return now - r.at < 4000; });
        if (!candidates.length) return null;
        // Carte de bibliothèque : l'élément porte son id dans le DOM.
        var anchor = options && options.positionTo;
        var card = anchor && anchor.closest ? anchor.closest('[data-id]') : null;
        var domId = card ? card.getAttribute('data-id') : null;
        for (var i = candidates.length - 1; i >= 0; i--) {
            if (!domId || candidates[i].item.Id === domId) return candidates[i].item;
        }
        return null;
    }

    function hookPlaybackManager() {
        if (!pm || pm.__jfplHooked) return;
        pm.__jfplHooked = true;
        var orig = pm.canPlay;
        pm.canPlay = function (item) {
            try { rememberItem(item); } catch (e) { /* ignore */ }
            return orig.apply(this, arguments);
        };
    }

    function hookActionSheet() {
        if (!actionSheetObj || actionSheetObj.__jfplHooked) return;
        actionSheetObj.__jfplHooked = true;
        var orig = actionSheetObj.show;
        actionSheetObj.show = function (options) {
            var self = this;
            var args = arguments;
            var items = options && options.items;
            if (!Array.isArray(items) || !me || !me.canEdit) return orig.apply(self, args);
            var isItemMenu = items.some(function (i) { return i && ITEM_MENU_IDS.indexOf(i.id) !== -1; });
            var item = isItemMenu ? menuItemFor(options) : null;
            if (!item || item.Type === 'CollectionFolder' || item.Type === 'UserView' || item.Type === 'Playlist' || item.Type === 'Person') return orig.apply(self, args);

            // Le menu est affiché une fraction de seconde plus tard, le temps de connaître l'état de l'élément.
            return apiJson('GET', 'PersonalLibrary/Me/Items/' + item.Id).then(function (state) {
                if (!state || !state.type || !state.canEdit) return orig.apply(self, args);
                var entry;
                if (state.mode === 'Only') {
                    entry = state.selected ? { id: 'jfpl-unkeep', name: t('menuUnkeep'), icon: 'remove_circle_outline' } : { id: 'jfpl-open', name: t('menuOpen'), icon: 'tune' };
                } else if (!state.selected) {
                    entry = { id: 'jfpl-hide', name: t('menuHide'), icon: 'visibility_off' };
                } else {
                    entry = { id: 'jfpl-open', name: t('menuOpen'), icon: 'tune' };
                }
                var newItems = items.slice();
                var del = newItems.map(function (i) { return i && i.id; }).indexOf('delete');
                newItems.splice(del !== -1 ? del : newItems.length, 0, entry);
                return orig.call(self, Object.assign({}, options, { items: newItems })).then(function (id) {
                    if (id && id.indexOf('jfpl-') === 0) {
                        menuAction(id, item, state);
                        // Pour itemContextMenu, c'est comme une fermeture sans choix.
                        throw new Error('ActionSheet closed without resolving');
                    }
                    return id;
                });
            }, function () { return orig.apply(self, args); });
        };
    }

    function menuAction(id, item, state) {
        if (id === 'jfpl-open') {
            openPage(state.type);
            return;
        }
        var p = id === 'jfpl-hide'
            ? (state.mode === 'All' ? setMode(state.type, 'Except') : Promise.resolve()).then(function () { return addRule(state.type, item.Id); })
            : removeRule(state.type, item.Id);
        // Appliqué tout de suite : la prochaine page affichée tient déjà compte du choix.
        p.then(function () { return apiJson('POST', 'PersonalLibrary/Me/Apply'); })
            .then(function () { toast(t(id === 'jfpl-hide' ? 'toastHidden' : 'toastRemoved', { name: item.Name })); })
            .then(function () { return loadMe(true); }).then(updateButton).catch(function (e) {
            console.error(TAG, 'menu', e);
            toast(t('error'));
        });
    }

    /* ------------------------------------------------------------------ */
    /*  6. Bouton d'en-tête                                                */
    /* ------------------------------------------------------------------ */

    // En-tête classique (10.11, mise en page « classique ») ou barre React (12.x), à côté de Rechercher ;
    // null = bouton flottant.
    function headerSlot() {
        var legacy = document.querySelector('.skinHeader .headerRight');
        if (legacy && legacy.offsetParent) {
            return { parent: legacy, before: legacy.querySelector('.headerUserButton'), template: null };
        }
        var search = document.querySelector('.MuiAppBar-root a[href*="search"], .MuiToolbar-root a[href*="search"]');
        if (search && search.offsetParent && search.parentElement) {
            return { parent: search.parentElement, before: search.nextSibling, template: search };
        }
        return null;
    }

    // Tableau de bord et lecteur vidéo (sans en-tête : le bouton flottant passerait par-dessus l'image).
    function noButtonPage() {
        return /^#\/(dashboard|configurationpage|plugins|users|libraries|networking|devices|tasks|logs|playback|branding|video)\b/i.test(location.hash);
    }

    function wanted() {
        return !!(me && currentUserId() && !noButtonPage() && (me.canEdit || me.types.some(function (x) { return x.mode !== 'All'; })));
    }

    function updateButton() {
        injectStyles();
        var btn = document.querySelector('.jfplHeaderButton');
        var floatBtn = document.querySelector('.jfpl-float');
        if (!wanted()) {
            if (btn) btn.remove();
            if (floatBtn) floatBtn.remove();
            return;
        }
        var slot = headerSlot();
        if (slot) {
            if (floatBtn) floatBtn.remove();
            if (btn && btn.parentElement !== slot.parent) {
                btn.remove();
                btn = null;
            }
            if (!btn) {
                btn = document.createElement('button');
                btn.type = 'button';
                if (slot.template) {
                    // En-tête React (MUI) : mêmes classes que le bouton Rechercher voisin.
                    btn.className = slot.template.className + ' jfplHeaderButton';
                } else {
                    btn.setAttribute('is', 'paper-icon-button-light');
                    btn.className = 'headerButton headerButtonRight jfplHeaderButton paper-icon-button-light';
                }
                btn.title = t('title');
                btn.setAttribute('aria-label', t('title'));
                btn.innerHTML = icon('tune');
                btn.addEventListener('click', function () { openPage(); });
                slot.parent.insertBefore(btn, slot.before);
            }
        } else if (!floatBtn) {
            floatBtn = document.createElement('button');
            floatBtn.className = 'jfpl-float';
            floatBtn.title = t('title');
            floatBtn.innerHTML = icon('tune');
            floatBtn.addEventListener('click', function () { openPage(); });
            document.body.appendChild(floatBtn);
        }
    }

    /* ------------------------------------------------------------------ */
    /*  7. Démarrage                                                       */
    /* ------------------------------------------------------------------ */

    var started = false;
    var lastUser = null;

    function onUser() {
        var uid = currentUserId();
        if (uid === lastUser) return;
        lastUser = uid;
        me = null;
        document.querySelectorAll('.jfplHeaderButton,.jfpl-float').forEach(function (b) { b.remove(); });
        if (!uid) return;
        loadMe(true).then(updateButton);
    }

    function start() {
        if (started) return;
        started = true;
        getWebpackRequire().then(function (req) {
            var lastCount = -1;
            var done = false;
            var scan = function () {
                if (done) return;
                var count = Object.keys(req.m || {}).length;
                if (count === lastCount) return;
                lastCount = count;
                scanModules(req);
                hookPlaybackManager();
                hookActionSheet();
                if (pm && actionSheetObj) {
                    done = true;
                    console.info(TAG, 'v' + VERSION + ' – ready');
                }
            };
            // actionSheet n'arrive qu'avec le chunk du menu contextuel, chargé au premier clic sur « ⋮ » :
            // on examine chaque chunk dès son arrivée, avant que le menu ne s'affiche.
            var chunks = self.webpackChunk;
            var origPush = chunks.push;
            chunks.push = function () {
                var r = origPush.apply(this, arguments);
                if (!done) Promise.resolve().then(scan);
                return r;
            };
            scan();
            var timer = setInterval(function () {
                scan();
                if (done) clearInterval(timer);
            }, 1000);
        });
        // Changement d'utilisateur ou de serveur : pas d'évènement public, on vérifie régulièrement.
        setInterval(onUser, 2000);
        onUser();
        // Tableau de bord, lecteur vidéo : pas de bouton ; on le remet en revenant.
        window.addEventListener('hashchange', function () { if (me) updateButton(); });
        // L'en-tête est reconstruit ou change (classique / React) selon les pages.
        var pending = false;
        new MutationObserver(function () {
            if (pending || !wanted()) return;
            pending = true;
            requestAnimationFrame(function () {
                pending = false;
                var btn = document.querySelector('.jfplHeaderButton');
                var slot = headerSlot();
                if (slot ? !btn || btn.parentElement !== slot.parent : !document.querySelector('.jfpl-float')) updateButton();
            });
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
