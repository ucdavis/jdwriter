import { BackendNav } from '@/features/backend/BackendNav.tsx';
import { createFileRoute, Outlet } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/backend')({
  component: BackendLayout,
});

function BackendLayout() {
  return (
    <>
      <BackendNav />
      <Outlet />
    </>
  );
}
