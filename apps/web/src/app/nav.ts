import {
  LayoutDashboard,
  ScrollText,
  Server,
  SlidersHorizontal,
  Smartphone,
  Users,
  Wrench,
  type LucideIcon,
} from 'lucide-react';

export interface NavItem {
  to: '/' | '/servers' | '/maintenance' | '/config' | '/versions' | '/staff' | '/audit';
  label: string;
  icon: LucideIcon;
  permission?: string;
}

export const NAV_GROUPS: { label?: string; items: NavItem[] }[] = [
  {
    items: [
      { to: '/', label: 'Overview', icon: LayoutDashboard },
      { to: '/servers', label: 'Servers', icon: Server, permission: 'servers.read' },
    ],
  },
  {
    label: 'Live ops',
    items: [
      { to: '/maintenance', label: 'Maintenance', icon: Wrench, permission: 'liveops.maintenance' },
      { to: '/config', label: 'Remote config', icon: SlidersHorizontal, permission: 'liveops.config' },
      { to: '/versions', label: 'App versions', icon: Smartphone, permission: 'versions.manage' },
    ],
  },
  {
    label: 'Administration',
    items: [
      { to: '/staff', label: 'Staff', icon: Users, permission: 'staff.manage' },
      { to: '/audit', label: 'Audit log', icon: ScrollText, permission: 'audit.read' },
    ],
  },
];
