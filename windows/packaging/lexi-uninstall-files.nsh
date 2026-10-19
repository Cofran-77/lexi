; Retired compatibility stub.
; build-installer.ps1 generates the uninstall list from the current clean staging
; directory and passes it to installer.nsi with /DUNINSTALL_LIST=...
; Do not include this repository file as installer content.
!error "lexi-uninstall-files.nsh is retired. Run build-installer.ps1 so the uninstall list is generated from clean staging."
